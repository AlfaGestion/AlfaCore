using System.Globalization;
using System.Net;
using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IContabilidadReportesService
{
    Task<ContabilidadCatalogos> GetCatalogosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContabilidadOpcion>> BuscarCuentasAsync(string texto, CancellationToken ct = default);
    Task<SumasSaldosResultado> GetSumasSaldosAsync(ContabilidadFiltro filtro, CancellationToken ct = default);
    Task<MayorResultado> GetMayorAsync(ContabilidadFiltro filtro, CancellationToken ct = default);
    Task<ContabilidadArchivo> ExportarSumasSaldosAsync(ContabilidadFiltro filtro, bool pdf, CancellationToken ct = default);
    Task<ContabilidadArchivo> ExportarMayorAsync(ContabilidadFiltro filtro, bool pdf, CancellationToken ct = default);
}

/// <summary>
/// Reportes contables de Gestión Contable → Reportes del legacy (cn000r): Balance de Sumas y Saldos
/// (D957004) y Reporte de Cuentas / Mayor (D957005), desde MV_ASIENTOS del ejercicio elegido.
/// Saldo anterior = movimientos del ejercicio con fecha anterior al «desde» (incluye la apertura).
/// </summary>
public sealed class ContabilidadReportesService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events, IDocumentPdfService pdf) : IContabilidadReportesService
{
    public const string MenuSumasSaldos = "D957004";
    public const string MenuMayor = "D957005";
    private const int MaximoMovimientosMayor = 20000;
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
    private static readonly string[] Proformas = ["FP", "NCFP", "NDFP", "CBFP", "FPC", "NCCP", "PGFP"];
    private sealed class ReglaContableException(string message) : InvalidOperationException(message);

    // ── Catálogos ───────────────────────────────────────────────────────────────────────────────

    public Task<ContabilidadCatalogos> GetCatalogosAsync(CancellationToken ct = default)
        => EjecutarAsync(null, "Catalogos", async cn => new ContabilidadCatalogos
        {
            Ejercicios = (await cn.QueryAsync<ContabilidadEjercicio>(new CommandDefinition("""
                SELECT CONVERT(int,PERIODO) Periodo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion, [FECHA DESDE] Desde, [FECHA HASTA] Hasta, CONVERT(bit,ISNULL(CERRADO,0)) Cerrado
                FROM dbo.MV_EJERCICIOS ORDER BY PERIODO DESC;
                """, cancellationToken: ct))).ToList(),
            Unidades = (await cn.QueryAsync<ContabilidadOpcion>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(Codigo)) Codigo, COALESCE(NULLIF(LTRIM(RTRIM(Descripcion)),''), LTRIM(RTRIM(Codigo))) Descripcion
                FROM dbo.V_TA_UnidadNegocio
                ORDER BY CASE WHEN ISNUMERIC(Codigo) = 1 THEN 0 ELSE 1 END, RIGHT(REPLICATE(' ', 8) + LTRIM(RTRIM(Codigo)), 8);
                """, cancellationToken: ct))).ToList(),
            PuedeVerProformas = await PuedeVerProformasAsync(cn, ct)
        }, ct);

    public Task<IReadOnlyList<ContabilidadOpcion>> BuscarCuentasAsync(string texto, CancellationToken ct = default)
        => EjecutarAsync<IReadOnlyList<ContabilidadOpcion>>(null, "BuscarCuentas", async cn =>
        {
            var t = (texto ?? string.Empty).Trim();
            return (await cn.QueryAsync<ContabilidadOpcion>(new CommandDefinition("""
                SELECT TOP (40) LTRIM(RTRIM(CODIGO)) Codigo,
                       LTRIM(RTRIM(CODIGO)) + ' · ' + LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) + CASE WHEN ISNULL(TITULO,0)=1 THEN ' (título)' ELSE '' END Descripcion
                FROM dbo.MA_CUENTAS
                WHERE @Texto = '' OR LTRIM(RTRIM(CODIGO)) LIKE @Prefijo OR DESCRIPCION LIKE @Like
                ORDER BY CASE WHEN LTRIM(RTRIM(CODIGO)) LIKE @Prefijo THEN 0 ELSE 1 END, LTRIM(RTRIM(CODIGO));
                """, new { Texto = t, Prefijo = t + "%", Like = "%" + t + "%" }, cancellationToken: ct))).ToList();
        }, ct);

    private static async Task<bool> PuedeVerProformasAsync(SqlConnection cn, CancellationToken ct, string? usuario = null)
    {
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN COL_LENGTH('dbo.TA_USUARIOS','VerProforma') IS NULL THEN 0 ELSE 1 END", cancellationToken: ct)) == 0)
            return false;
        return usuario is not null && await cn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT TOP (1) CONVERT(bit,ISNULL(VerProforma,0)) FROM dbo.TA_USUARIOS WHERE LTRIM(RTRIM(NOMBRE)) = @Usuario;",
            new { Usuario = usuario }, cancellationToken: ct));
    }

    // ── Filtro común ────────────────────────────────────────────────────────────────────────────

    private async Task<(ContabilidadEjercicio Ejercicio, DynamicParameters Parametros, string Where)> PrepararAsync(SqlConnection cn, ContabilidadFiltro f, CancellationToken ct)
    {
        var ejercicio = await cn.QuerySingleOrDefaultAsync<ContabilidadEjercicio>(new CommandDefinition("""
            SELECT CONVERT(int,PERIODO) Periodo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion, [FECHA DESDE] Desde, [FECHA HASTA] Hasta, CONVERT(bit,ISNULL(CERRADO,0)) Cerrado
            FROM dbo.MV_EJERCICIOS WHERE PERIODO = @Periodo;
            """, new { f.Periodo }, cancellationToken: ct)) ?? throw new ReglaContableException("Elegí un ejercicio.");
        f.Desde = f.Desde.Date < ejercicio.Desde.Date ? ejercicio.Desde.Date : f.Desde.Date;
        f.Hasta = f.Hasta == default || f.Hasta.Date > ejercicio.Hasta.Date ? ejercicio.Hasta.Date : f.Hasta.Date;
        if (f.Hasta < f.Desde) throw new ReglaContableException("La fecha hasta es anterior a la fecha desde.");
        if (f.IncluirProformas && !await PuedeVerProformasAsync(cn, ct, users.GetCurrentUserName()))
            throw new ReglaContableException("Tu usuario no puede ver proformas: desmarcá «Incluir proformas».");
        var unidades = f.Unidades.Select(u => u.Trim()).Where(u => u.Length > 0).Distinct().ToArray();
        var p = new DynamicParameters(new { f.Periodo, f.Desde, f.Hasta, Unidades = unidades, SinUnidad = unidades.Length == 0, Proformas });
        const string where = """
            a.PERIODO = @Periodo AND a.FECHA < DATEADD(day, 1, @Hasta)
            AND (@SinUnidad = 1 OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) IN @Unidades)
            """;
        return (ejercicio, p, f.IncluirProformas ? where : where + " AND LTRIM(RTRIM(ISNULL(a.TC,''))) NOT IN @Proformas");
    }

    private static async Task<string> EmpresaAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'NOMBRE';", cancellationToken: ct)) ?? string.Empty;

    private sealed record Cuenta(string Codigo, string Descripcion, bool Titulo, string TipoVista);

    private static async Task<List<Cuenta>> PlanAsync(SqlConnection cn, CancellationToken ct)
        => (await cn.QueryAsync<Cuenta>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion, CONVERT(bit,ISNULL(TITULO,0)) Titulo, UPPER(LTRIM(RTRIM(ISNULL(TipoVista,'')))) TipoVista
            FROM dbo.MA_CUENTAS WHERE ISNULL(LTRIM(RTRIM(CODIGO)),'') <> '';
            """, cancellationToken: ct))).ToList();

    // ── Balance de sumas y saldos ───────────────────────────────────────────────────────────────

    public Task<SumasSaldosResultado> GetSumasSaldosAsync(ContabilidadFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(MenuSumasSaldos, "SumasSaldos", cn => LeerSumasSaldosAsync(cn, filtro, ct), ct);

    private async Task<SumasSaldosResultado> LeerSumasSaldosAsync(SqlConnection cn, ContabilidadFiltro f, CancellationToken ct)
    {
        var (ejercicio, p, where) = await PrepararAsync(cn, f, ct);
        var saldos = (await cn.QueryAsync<(string Cuenta, decimal Anterior, decimal Debe, decimal Haber)>(new CommandDefinition($"""
            SELECT LTRIM(RTRIM(a.CUENTA)),
                SUM(CASE WHEN a.FECHA < @Desde THEN CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END ELSE 0 END),
                SUM(CASE WHEN a.FECHA >= @Desde AND a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE 0 END),
                SUM(CASE WHEN a.FECHA >= @Desde AND a.[DEBE-HABER] <> 'D' THEN a.IMPORTE ELSE 0 END)
            FROM dbo.MV_ASIENTOS a WHERE {where}
            GROUP BY LTRIM(RTRIM(a.CUENTA));
            """, p, commandTimeout: 120, cancellationToken: ct))).ToList();
        var desbalanceados = (await cn.QueryAsync<AsientoDesbalanceado>(new CommandDefinition($"""
            SELECT CONVERT(int,a.MES_OPERATIVO) MesOperativo, CONVERT(int,a.[NUMERO ASIENTO]) Numero, MIN(a.FECHA) Fecha, MIN(LTRIM(RTRIM(ISNULL(a.TC,'')))) Tc,
                   MIN(LTRIM(RTRIM(ISNULL(a.SUCURSAL,''))) + '-' + LTRIM(RTRIM(ISNULL(a.NUMERO,''))) + ' ' + LTRIM(RTRIM(ISNULL(a.LETRA,'')))) Comprobante,
                   MIN(LEFT(LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(300), a.DETALLE),''))), 120)) Detalle, COUNT(*) Renglones,
                   SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END) Diferencia
            FROM dbo.MV_ASIENTOS a WHERE {where} AND a.FECHA >= @Desde
            GROUP BY a.MES_OPERATIVO, a.[NUMERO ASIENTO]
            HAVING ABS(SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END)) >= 0.005
            ORDER BY MIN(a.FECHA), a.MES_OPERATIVO, a.[NUMERO ASIENTO];
            """, p, commandTimeout: 120, cancellationToken: ct))).ToList();
        var plan = await PlanAsync(cn, ct);
        return new SumasSaldosResultado
        {
            Filtro = f, Empresa = await EmpresaAsync(cn, ct), Ejercicio = ejercicio.Descripcion,
            Filas = ArmarSumasSaldos(plan.Select(c => (c.Codigo, c.Descripcion, c.Titulo, c.TipoVista)).ToList(), saldos, f.AgruparTerceros, f.MostrarTitulos, f.IncluirSinMovimientos),
            TotalSaldoAnterior = saldos.Sum(s => s.Anterior), TotalDebe = saldos.Sum(s => s.Debe), TotalHaber = saldos.Sum(s => s.Haber),
            Desbalanceados = desbalanceados
        };
    }

    /// <summary>Arma el balance: cuentas imputables con saldo; clientes/proveedores (CL/PR) agrupados en su
    /// cuenta título si se pide; los títulos suman a todas las cuentas cuyo código empieza con el suyo.</summary>
    internal static List<SumasSaldosFila> ArmarSumasSaldos(IReadOnlyList<(string Codigo, string Descripcion, bool Titulo, string TipoVista)> plan,
        IReadOnlyList<(string Cuenta, decimal Anterior, decimal Debe, decimal Haber)> saldos, bool agruparTerceros, bool mostrarTitulos, bool sinMovimientos)
    {
        var porCodigo = plan.GroupBy(c => c.Codigo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var titulos = plan.Where(c => c.Titulo).OrderByDescending(c => c.Codigo.Length).ToList();
        var largos = plan.Select(c => c.Codigo.Length).Distinct().OrderBy(x => x).ToList();
        string? Padre(string codigo) => titulos.FirstOrDefault(t => t.Codigo.Length < codigo.Length && codigo.StartsWith(t.Codigo, StringComparison.OrdinalIgnoreCase)).Codigo;

        var filas = new Dictionary<string, SumasSaldosFila>(StringComparer.OrdinalIgnoreCase);
        void Sumar(string clave, string codigo, string descripcion, int nivel, decimal ant, decimal debe, decimal haber, bool agrupada)
        {
            if (!filas.TryGetValue(clave, out var fila))
                filas[clave] = fila = new SumasSaldosFila { Codigo = codigo, Descripcion = descripcion, Nivel = nivel };
            fila.SaldoAnterior += ant; fila.Debe += debe; fila.Haber += haber;
            if (agrupada) fila.Agrupadas++;
        }
        var conSaldo = saldos.Where(s => s.Anterior != 0 || s.Debe != 0 || s.Haber != 0).ToList();
        var imputables = conSaldo.Select(s => (s.Cuenta, s.Anterior, s.Debe, s.Haber)).ToList();
        if (sinMovimientos)
            imputables.AddRange(plan.Where(c => !c.Titulo && conSaldo.All(s => !s.Cuenta.Equals(c.Codigo, StringComparison.OrdinalIgnoreCase)))
                .Select(c => (c.Codigo, 0m, 0m, 0m)));
        foreach (var (cuenta, ant, debe, haber) in imputables)
        {
            var datos = porCodigo.GetValueOrDefault(cuenta);
            var nivel = Math.Max(0, largos.IndexOf(cuenta.Length));
            if (agruparTerceros && datos.TipoVista is "CL" or "PR" && Padre(cuenta) is { } padre)
            {
                var desc = datos.TipoVista == "CL" ? "Clientes" : "Proveedores";
                Sumar(padre + "~" + datos.TipoVista, padre, $"{porCodigo[padre].Descripcion} · {desc}", Math.Max(0, largos.IndexOf(cuenta.Length)), ant, debe, haber, true);
            }
            else Sumar(cuenta, cuenta, datos.Descripcion ?? "(cuenta inexistente en el plan)", nivel, ant, debe, haber, false);
        }
        var lista = filas.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
        if (!mostrarTitulos) return lista;
        foreach (var t in titulos)
        {
            var hijas = lista.Where(r => !r.EsTitulo && r.Codigo.StartsWith(t.Codigo, StringComparison.OrdinalIgnoreCase) && r.Codigo.Length >= t.Codigo.Length
                && !(r.Codigo.Equals(t.Codigo, StringComparison.OrdinalIgnoreCase) && r.Agrupadas == 0)).ToList();
            if (hijas.Count == 0) continue;
            lista.Add(new SumasSaldosFila
            {
                Codigo = t.Codigo, Descripcion = t.Descripcion, EsTitulo = true, Nivel = Math.Max(0, largos.IndexOf(t.Codigo.Length)),
                SaldoAnterior = hijas.Sum(h => h.SaldoAnterior), Debe = hijas.Sum(h => h.Debe), Haber = hijas.Sum(h => h.Haber)
            });
        }
        // Orden del plan: el título antes que sus cuentas (y antes que su renglón de clientes/proveedores agrupados).
        return lista.OrderBy(r => r.Codigo + (r.EsTitulo ? "" : r.Agrupadas > 0 ? "~" : ""), StringComparer.Ordinal).ToList();
    }

    // ── Mayor ───────────────────────────────────────────────────────────────────────────────────

    public Task<MayorResultado> GetMayorAsync(ContabilidadFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(MenuMayor, "Mayor", cn => LeerMayorAsync(cn, filtro, ct), ct);

    private async Task<MayorResultado> LeerMayorAsync(SqlConnection cn, ContabilidadFiltro f, CancellationToken ct)
    {
        var desde = (f.CuentaDesde ?? string.Empty).Trim(); var hasta = (f.CuentaHasta ?? string.Empty).Trim();
        if (desde.Length == 0) throw new ReglaContableException("Elegí la cuenta (o el título, para ver todas sus cuentas).");
        var (ejercicio, p, where) = await PrepararAsync(cn, f, ct);
        // Sin «hasta»: la cuenta elegida y, si es un título, todas las que empiezan con su código.
        var filtroCuenta = hasta.Length == 0
            ? "LTRIM(RTRIM(a.CUENTA)) LIKE @Prefijo"
            : "LTRIM(RTRIM(a.CUENTA)) BETWEEN @CuentaDesde AND @CuentaHasta";
        p.Add("Prefijo", desde + "%"); p.Add("CuentaDesde", desde); p.Add("CuentaHasta", hasta.Length == 0 ? desde : hasta);
        p.Add("Maximo", MaximoMovimientosMayor + 1);
        var anteriores = (await cn.QueryAsync<(string Cuenta, decimal Saldo)>(new CommandDefinition($"""
            SELECT LTRIM(RTRIM(a.CUENTA)), SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END)
            FROM dbo.MV_ASIENTOS a WHERE {where} AND a.FECHA < @Desde AND {filtroCuenta}
            GROUP BY LTRIM(RTRIM(a.CUENTA));
            """, p, commandTimeout: 120, cancellationToken: ct))).ToDictionary(x => x.Cuenta, x => x.Saldo, StringComparer.OrdinalIgnoreCase);
        var movimientos = (await cn.QueryAsync<MovSql>(new CommandDefinition($"""
            SELECT TOP (@Maximo) LTRIM(RTRIM(a.CUENTA)) Cuenta, a.FECHA Fecha, CONVERT(int,a.MES_OPERATIVO) MesOperativo, CONVERT(int,a.[NUMERO ASIENTO]) Asiento,
                   LTRIM(RTRIM(ISNULL(a.TC,''))) Tc,
                   CASE WHEN ISNULL(LTRIM(RTRIM(a.NUMERO)),'') = '' THEN '' ELSE LTRIM(RTRIM(ISNULL(a.SUCURSAL,''))) + '-' + LTRIM(RTRIM(a.NUMERO)) + ' ' + LTRIM(RTRIM(ISNULL(a.LETRA,''))) END Comprobante,
                   LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(500), a.DETALLE),''))) Detalle, LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) Unidad,
                   CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE 0 END Debe, CASE WHEN a.[DEBE-HABER] <> 'D' THEN a.IMPORTE ELSE 0 END Haber
            FROM dbo.MV_ASIENTOS a WHERE {where} AND a.FECHA >= @Desde AND {filtroCuenta}
            ORDER BY LTRIM(RTRIM(a.CUENTA)), a.FECHA, a.MES_OPERATIVO, a.[NUMERO ASIENTO], a.SECUENCIA;
            """, p, commandTimeout: 120, cancellationToken: ct))).ToList();
        var truncado = movimientos.Count > MaximoMovimientosMayor;
        if (truncado) movimientos = movimientos.Take(MaximoMovimientosMayor).ToList();
        var plan = (await PlanAsync(cn, ct)).GroupBy(c => c.Codigo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Descripcion, StringComparer.OrdinalIgnoreCase);
        var cuentas = anteriores.Keys.Concat(movimientos.Select(m => m.Cuenta)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal)
            .Select(c =>
            {
                var cuenta = new MayorCuenta { Codigo = c, Descripcion = plan.GetValueOrDefault(c, "(cuenta inexistente en el plan)"), SaldoAnterior = anteriores.GetValueOrDefault(c) };
                var saldo = cuenta.SaldoAnterior;
                foreach (var m in movimientos.Where(m => m.Cuenta.Equals(c, StringComparison.OrdinalIgnoreCase)))
                {
                    saldo += m.Debe - m.Haber;
                    cuenta.Movimientos.Add(new MayorMovimiento { Fecha = m.Fecha, MesOperativo = m.MesOperativo, Asiento = m.Asiento, Tc = m.Tc, Comprobante = m.Comprobante,
                        Detalle = m.Detalle, Unidad = m.Unidad, Debe = m.Debe, Haber = m.Haber, Saldo = saldo });
                }
                return cuenta;
            })
            .Where(c => c.SaldoAnterior != 0 || c.Movimientos.Count > 0).ToList();
        return new MayorResultado { Filtro = f, Empresa = await EmpresaAsync(cn, ct), Ejercicio = ejercicio.Descripcion, Cuentas = cuentas, Truncado = truncado };
    }

    private sealed class MovSql
    {
        public string Cuenta { get; set; } = ""; public DateTime Fecha { get; set; } public int MesOperativo { get; set; } public int Asiento { get; set; }
        public string Tc { get; set; } = ""; public string Comprobante { get; set; } = ""; public string Detalle { get; set; } = ""; public string Unidad { get; set; } = "";
        public decimal Debe { get; set; } public decimal Haber { get; set; }
    }

    // ── Exportaciones ───────────────────────────────────────────────────────────────────────────

    public Task<ContabilidadArchivo> ExportarSumasSaldosAsync(ContabilidadFiltro filtro, bool comoPdf, CancellationToken ct = default)
        => EjecutarAsync(MenuSumasSaldos, "ExportarSumasSaldos", async cn =>
        {
            var r = await LeerSumasSaldosAsync(cn, filtro, ct);
            var nombre = $"SumasYSaldos_{r.Filtro.Desde:yyyyMMdd}_{r.Filtro.Hasta:yyyyMMdd}";
            if (comoPdf) return new ContabilidadArchivo(nombre + ".pdf", "application/pdf", await pdf.GenerateAsync(HtmlSumasSaldos(r), new DocumentPdfFooterOptions(true, true, r.Empresa), ct));
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Sumas y saldos");
            Encabezado(ws, "Balance de sumas y saldos", r.Empresa, r.Ejercicio, r.Filtro);
            string[] t = ["Cuenta", "Descripción", "Saldo anterior", "Debe", "Haber", "Saldo"];
            for (var c = 0; c < t.Length; c++) ws.Cell(4, c + 1).Value = t[c];
            Titulos(ws, 4, t.Length);
            var fila = 5;
            foreach (var x in r.Filas)
            {
                ws.Cell(fila, 1).Value = x.Codigo; ws.Cell(fila, 2).Value = new string(' ', x.Nivel * 2) + x.Descripcion + (x.Agrupadas > 0 ? $" ({x.Agrupadas})" : "");
                ws.Cell(fila, 3).Value = x.SaldoAnterior; ws.Cell(fila, 4).Value = x.Debe; ws.Cell(fila, 5).Value = x.Haber; ws.Cell(fila, 6).Value = x.Saldo;
                if (x.EsTitulo) ws.Row(fila).Style.Font.Bold = true;
                fila++;
            }
            ws.Cell(fila, 2).Value = "Totales"; ws.Row(fila).Style.Font.Bold = true;
            ws.Cell(fila, 3).Value = r.TotalSaldoAnterior; ws.Cell(fila, 4).Value = r.TotalDebe; ws.Cell(fila, 5).Value = r.TotalHaber; ws.Cell(fila, 6).Value = r.TotalSaldo;
            ws.Range(5, 3, fila, 6).Style.NumberFormat.Format = "#,##0.00";
            if (r.Desbalanceados.Count > 0)
            {
                var d = wb.Worksheets.Add("Asientos desbalanceados");
                string[] h = ["Fecha", "Mes", "Asiento", "TC", "Comprobante", "Detalle", "Renglones", "Diferencia"];
                for (var c = 0; c < h.Length; c++) d.Cell(1, c + 1).Value = h[c];
                Titulos(d, 1, h.Length);
                var i = 2;
                foreach (var a in r.Desbalanceados)
                {
                    d.Cell(i, 1).Value = a.Fecha; d.Cell(i, 1).Style.DateFormat.Format = "dd/MM/yyyy";
                    d.Cell(i, 2).Value = a.MesOperativo; d.Cell(i, 3).Value = a.Numero; d.Cell(i, 4).Value = a.Tc; d.Cell(i, 5).Value = a.Comprobante;
                    d.Cell(i, 6).Value = a.Detalle; d.Cell(i, 7).Value = a.Renglones; d.Cell(i, 8).Value = a.Diferencia; i++;
                }
                d.Column(8).Style.NumberFormat.Format = "#,##0.00"; d.Columns().AdjustToContents();
            }
            ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(4);
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            return new ContabilidadArchivo(nombre + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray());
        }, ct);

    public Task<ContabilidadArchivo> ExportarMayorAsync(ContabilidadFiltro filtro, bool comoPdf, CancellationToken ct = default)
        => EjecutarAsync(MenuMayor, "ExportarMayor", async cn =>
        {
            var r = await LeerMayorAsync(cn, filtro, ct);
            var nombre = $"Mayor_{r.Filtro.CuentaDesde}_{r.Filtro.Desde:yyyyMMdd}_{r.Filtro.Hasta:yyyyMMdd}";
            if (comoPdf) return new ContabilidadArchivo(nombre + ".pdf", "application/pdf", await pdf.GenerateAsync(HtmlMayor(r), new DocumentPdfFooterOptions(true, true, r.Empresa), ct));
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Mayor");
            Encabezado(ws, "Mayor de cuentas", r.Empresa, r.Ejercicio, r.Filtro);
            string[] t = ["Fecha", "Asiento", "TC", "Comprobante", "Detalle", "Debe", "Haber", "Saldo"];
            for (var c = 0; c < t.Length; c++) ws.Cell(4, c + 1).Value = t[c];
            Titulos(ws, 4, t.Length);
            var fila = 5;
            foreach (var cuenta in r.Cuentas)
            {
                ws.Cell(fila, 1).Value = $"{cuenta.Codigo} · {cuenta.Descripcion}"; ws.Row(fila).Style.Font.Bold = true;
                ws.Cell(fila, 5).Value = "Saldo anterior"; ws.Cell(fila, 8).Value = cuenta.SaldoAnterior; fila++;
                foreach (var m in cuenta.Movimientos)
                {
                    ws.Cell(fila, 1).Value = m.Fecha; ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";
                    ws.Cell(fila, 2).Value = $"{m.MesOperativo}-{m.Asiento}"; ws.Cell(fila, 3).Value = m.Tc; ws.Cell(fila, 4).Value = m.Comprobante;
                    ws.Cell(fila, 5).Value = m.Detalle; ws.Cell(fila, 6).Value = m.Debe; ws.Cell(fila, 7).Value = m.Haber; ws.Cell(fila, 8).Value = m.Saldo; fila++;
                }
                ws.Cell(fila, 5).Value = "Total de la cuenta"; ws.Cell(fila, 6).Value = cuenta.Debe; ws.Cell(fila, 7).Value = cuenta.Haber; ws.Cell(fila, 8).Value = cuenta.Saldo;
                ws.Row(fila).Style.Font.Bold = true; fila += 2;
            }
            ws.Range(5, 6, fila, 8).Style.NumberFormat.Format = "#,##0.00";
            ws.Columns().AdjustToContents(); ws.Column(5).Width = Math.Min(ws.Column(5).Width, 60); ws.SheetView.FreezeRows(4);
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            return new ContabilidadArchivo(nombre + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray());
        }, ct);

    private static void Encabezado(IXLWorksheet ws, string titulo, string empresa, string ejercicio, ContabilidadFiltro f)
    {
        ws.Cell(1, 1).Value = $"{titulo} · {empresa}"; ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(2, 1).Value = $"{ejercicio} · {f.Desde:dd/MM/yyyy} al {f.Hasta:dd/MM/yyyy}{(f.Unidades.Count > 0 ? " · Unidades " + string.Join(", ", f.Unidades) : "")}{(f.IncluirProformas ? " · Incluye proformas" : "")}";
    }

    private static void Titulos(IXLWorksheet ws, int fila, int columnas)
    {
        ws.Range(fila, 1, fila, columnas).Style.Font.Bold = true;
        ws.Range(fila, 1, fila, columnas).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8ECF2");
    }

    private const string CssPdf = """
        @page { size: A4; margin: 10mm 8mm 14mm 8mm; }
        body { font-family: Arial, sans-serif; font-size: 8pt; color: #111; margin: 0; }
        h1 { font-size: 12pt; margin: 0 0 2px; } .sub { color: #444; margin-bottom: 6px; }
        table { width: 100%; border-collapse: collapse; } thead { display: table-header-group; }
        th { background: #e8ecf2; text-align: left; padding: 3px; border-bottom: 1px solid #999; font-size: 7.5pt; }
        td { padding: 2px 3px; border-bottom: 1px solid #e6e6e6; vertical-align: top; } .n { text-align: right; white-space: nowrap; }
        tr { page-break-inside: avoid; } .t td { font-weight: bold; background: #f3f5f8; } .tot td { font-weight: bold; border-top: 1px solid #333; }
        .cta td { font-weight: bold; background: #eef1f6; padding-top: 5px; } .aviso { margin-top: 10px; color: #a00; }
        """;

    private static string M(decimal v) => v == 0 ? "" : v.ToString("N2", Ar);
    private static string E(string s) => WebUtility.HtmlEncode(s);

    internal static string HtmlSumasSaldos(SumasSaldosResultado r)
    {
        var sb = new StringBuilder($"<!doctype html><html><head><meta charset=\"utf-8\"><style>{CssPdf}</style></head><body>");
        sb.Append($"<h1>Balance de sumas y saldos</h1><div class=\"sub\">{E(r.Empresa)} · {E(r.Ejercicio)} · {r.Filtro.Desde:dd/MM/yyyy} al {r.Filtro.Hasta:dd/MM/yyyy}</div>");
        sb.Append("<table><thead><tr><th>Cuenta</th><th>Descripción</th><th class=\"n\">Saldo anterior</th><th class=\"n\">Debe</th><th class=\"n\">Haber</th><th class=\"n\">Saldo</th></tr></thead><tbody>");
        foreach (var x in r.Filas)
            sb.Append($"<tr class=\"{(x.EsTitulo ? "t" : "")}\"><td>{E(x.Codigo)}</td><td style=\"padding-left:{3 + x.Nivel * 8}px\">{E(x.Descripcion)}{(x.Agrupadas > 0 ? $" ({x.Agrupadas})" : "")}</td>")
              .Append($"<td class=\"n\">{M(x.SaldoAnterior)}</td><td class=\"n\">{M(x.Debe)}</td><td class=\"n\">{M(x.Haber)}</td><td class=\"n\">{M(x.Saldo)}</td></tr>");
        sb.Append($"<tr class=\"tot\"><td></td><td>Totales</td><td class=\"n\">{M(r.TotalSaldoAnterior)}</td><td class=\"n\">{M(r.TotalDebe)}</td><td class=\"n\">{M(r.TotalHaber)}</td><td class=\"n\">{M(r.TotalSaldo)}</td></tr></tbody></table>");
        if (r.Desbalanceados.Count > 0)
            sb.Append($"<div class=\"aviso\">Atención: {r.Desbalanceados.Count} asiento(s) del período no balancean (diferencia total {r.Desbalanceados.Sum(a => a.Diferencia).ToString("N2", Ar)}). El detalle está en la pantalla y en el Excel.</div>");
        return sb.Append("</body></html>").ToString();
    }

    internal static string HtmlMayor(MayorResultado r)
    {
        var sb = new StringBuilder($"<!doctype html><html><head><meta charset=\"utf-8\"><style>{CssPdf}</style></head><body>");
        sb.Append($"<h1>Mayor de cuentas</h1><div class=\"sub\">{E(r.Empresa)} · {E(r.Ejercicio)} · {r.Filtro.Desde:dd/MM/yyyy} al {r.Filtro.Hasta:dd/MM/yyyy}</div>");
        sb.Append("<table><thead><tr><th>Fecha</th><th>Asiento</th><th>Comprobante</th><th>Detalle</th><th class=\"n\">Debe</th><th class=\"n\">Haber</th><th class=\"n\">Saldo</th></tr></thead><tbody>");
        foreach (var c in r.Cuentas)
        {
            sb.Append($"<tr class=\"cta\"><td colspan=\"4\">{E(c.Codigo)} · {E(c.Descripcion)} — saldo anterior</td><td></td><td></td><td class=\"n\">{c.SaldoAnterior.ToString("N2", Ar)}</td></tr>");
            foreach (var m in c.Movimientos)
                sb.Append($"<tr><td>{m.Fecha:dd/MM/yy}</td><td>{m.MesOperativo}-{m.Asiento}</td><td>{E(m.Tc)} {E(m.Comprobante)}</td><td>{E(m.Detalle)}</td><td class=\"n\">{M(m.Debe)}</td><td class=\"n\">{M(m.Haber)}</td><td class=\"n\">{m.Saldo.ToString("N2", Ar)}</td></tr>");
            sb.Append($"<tr class=\"tot\"><td colspan=\"4\">Total {E(c.Codigo)}</td><td class=\"n\">{M(c.Debe)}</td><td class=\"n\">{M(c.Haber)}</td><td class=\"n\">{c.Saldo.ToString("N2", Ar)}</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (r.Truncado) sb.Append($"<div class=\"aviso\">Se muestran los primeros {MaximoMovimientosMayor:N0} movimientos: acotá las cuentas o las fechas.</div>");
        return sb.Append("</body></html>").ToString();
    }

    // ── Ejecución con permiso ───────────────────────────────────────────────────────────────────

    private async Task<T> EjecutarAsync<T>(string? menu, string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var id))
            throw new ReglaContableException("Ingresá en la base activa para consultar los reportes contables.");
        try
        {
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            if (menu is not null && !await permissions.HasAccessAsync(menu, ct))
                throw new ReglaContableException("Tu usuario no tiene permiso para este reporte contable.");
            if (menu is null && !await permissions.HasAccessAsync(MenuMayor, ct) && !await permissions.HasAccessAsync(MenuSumasSaldos, ct))
                throw new ReglaContableException("Tu usuario no tiene permiso para los reportes contables.");
            var r = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != id)
                throw new ReglaContableException("Cambió la sesión. Volvé a generar el reporte en la base activa.");
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaContableException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("ContabilidadReportes", accion, ex, "No se pudo generar el reporte contable.", ct: ct);
            throw new AppUserFacingException("No se pudo generar el reporte contable.", codigo, ex);
        }
    }
}
