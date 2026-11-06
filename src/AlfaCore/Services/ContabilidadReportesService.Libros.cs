using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Libro diario (Registraciones, D957002), Subdiarios (D957003), Plan de cuentas (D957001) y Balance general
/// de saldos (D957006) de Gestión Contable → Reportes del legacy.
/// </summary>
public sealed partial class ContabilidadReportesService
{
    internal const int MaximoRenglonesDiario = 30000;

    private static async Task<List<ContabilidadOpcion>> SubdiariosAsync(SqlConnection cn, CancellationToken ct)
    {
        // Los de la tabla y los que aparecen en asientos (TIPO_REG), aunque no estén en la tabla (ej. VTAS).
        var lista = (await cn.QueryAsync<ContabilidadOpcion>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) Codigo, MAX(LTRIM(RTRIM(ISNULL(DESCRIPCION,'')))) Descripcion FROM dbo.MA_SUBDIARIOS
            WHERE ISNULL(LTRIM(RTRIM(CODIGO)),'') <> '' GROUP BY LTRIM(RTRIM(CODIGO))
            UNION
            SELECT DISTINCT LTRIM(RTRIM(a.TIPO_REG)), LTRIM(RTRIM(a.TIPO_REG)) FROM dbo.MV_ASIENTOS a
            WHERE ISNULL(LTRIM(RTRIM(a.TIPO_REG)),'') NOT IN ('', CHAR(255) + 'RS')
              AND NOT EXISTS (SELECT 1 FROM dbo.MA_SUBDIARIOS s WHERE LTRIM(RTRIM(s.CODIGO)) = LTRIM(RTRIM(a.TIPO_REG)));
            """, commandTimeout: 60, cancellationToken: ct))).ToList();
        return lista.GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(x => x.Descripcion.Length).First())
            .OrderBy(x => x.Codigo).ToList();
    }

    // ── Libro diario y subdiarios ───────────────────────────────────────────────────────────────

    public Task<DiarioResultado> GetDiarioAsync(ContabilidadFiltro filtro, bool subdiarios, CancellationToken ct = default)
        => EjecutarAsync(subdiarios ? MenuSubdiarios : MenuDiario, subdiarios ? "Subdiarios" : "Diario", cn => LeerDiarioAsync(cn, filtro, subdiarios, ct), ct);

    private async Task<DiarioResultado> LeerDiarioAsync(SqlConnection cn, ContabilidadFiltro f, bool subdiarios, CancellationToken ct)
    {
        var (ejercicio, p, where) = await PrepararAsync(cn, f, ct);
        var sub = (f.Subdiario ?? string.Empty).Trim();
        if (subdiarios && sub.Length == 0) throw new ReglaContableException("Elegí el subdiario.");
        // TIPO_REG: código del subdiario; vacío (o el resumen «ÿRS») = diario general.
        // El legacy reutiliza mes y número entre comprobantes: conservar la identidad del movimiento.
        var mismoAsiento = $"""
            s.PERIODO = a.PERIODO AND s.MES_OPERATIVO = a.MES_OPERATIVO
            AND (({EsComprobanteContableSql}) OR s.[NUMERO ASIENTO] = a.[NUMERO ASIENTO])
            AND s.TC = a.TC AND s.SUCURSAL = a.SUCURSAL AND s.NUMERO = a.NUMERO AND s.LETRA = a.LETRA
            AND ISNULL(s.TIPO_REG,'') = ISNULL(a.TIPO_REG,'')
            """;
        var filtroSub = sub.Equals("DG", StringComparison.OrdinalIgnoreCase) || (!subdiarios && f.ExcluirSubdiarios)
            ? $" AND NOT EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS s WHERE {mismoAsiento} AND ISNULL(LTRIM(RTRIM(s.TIPO_REG)),'') NOT IN ('', CHAR(255) + 'RS'))"
            : sub.Length > 0 ? $" AND EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS s WHERE {mismoAsiento} AND LTRIM(RTRIM(s.TIPO_REG)) = @Subdiario)" : "";
        var excluirTcs = (f.ExcluirTcs ?? []).Select(t => t.Trim().ToUpperInvariant()).Where(t => t.Length > 0).Distinct().ToArray();
        if (excluirTcs.Length > 0) { filtroSub += " AND UPPER(LTRIM(RTRIM(ISNULL(a.TC,'')))) NOT IN @ExcluirTcs"; p.Add("ExcluirTcs", excluirTcs); }
        p.Add("Subdiario", sub); p.Add("Maximo", MaximoRenglonesDiario + 1);
        var cuentas = (await PlanAsync(cn, ct)).GroupBy(c => c.Codigo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Descripcion, StringComparer.OrdinalIgnoreCase);
        var renglones = (await cn.QueryAsync<RenglonSql>(new CommandDefinition($"""
            SELECT TOP (@Maximo) a.FECHA Fecha, CONVERT(int,a.MES_OPERATIVO) MesOperativo, CONVERT(int,a.[NUMERO ASIENTO]) Numero,
                   LTRIM(RTRIM(ISNULL(a.TC,''))) Tc,
                   CONVERT(bit,CASE WHEN {EsComprobanteContableSql} THEN 1 ELSE 0 END) TieneComprobante,
                   CONVERT(bit,CASE WHEN LTRIM(RTRIM(ISNULL(a.CABCUENTA,''))) <> '' THEN 1 ELSE 0 END) EsCabecera,
                   CASE WHEN ISNULL(LTRIM(RTRIM(a.NUMERO)),'') = '' THEN '' ELSE LTRIM(RTRIM(ISNULL(a.SUCURSAL,''))) + '-' + LTRIM(RTRIM(a.NUMERO)) + ' ' + LTRIM(RTRIM(ISNULL(a.LETRA,''))) END Comprobante,
                   LTRIM(RTRIM(ISNULL(a.TIPO_REG,''))) Subdiario, LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) Unidad,
                   LTRIM(RTRIM(a.CUENTA)) Cuenta, LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(500), a.DETALLE),''))) Detalle,
                   CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE 0 END Debe, CASE WHEN a.[DEBE-HABER] <> 'D' THEN a.IMPORTE ELSE 0 END Haber
            FROM dbo.MV_ASIENTOS a WHERE {where} AND a.FECHA >= @Desde{filtroSub}
            ORDER BY a.FECHA, a.MES_OPERATIVO, a.TIPO_REG, a.TC, a.SUCURSAL, a.NUMERO, a.LETRA, a.[NUMERO ASIENTO], a.SECUENCIA;
            """, p, commandTimeout: 120, cancellationToken: ct))).ToList();
        var truncado = renglones.Count > MaximoRenglonesDiario;
        if (truncado) renglones = renglones.Take(MaximoRenglonesDiario).ToList();
        var asientos = renglones.GroupBy(r => (r.MesOperativo, Numero: r.TieneComprobante ? 0 : r.Numero, r.Tc, r.Comprobante, r.Subdiario))
            .Select(g =>
            {
                var primero = g.FirstOrDefault(r => r.EsCabecera) ?? g.First();
                return new DiarioAsiento
                {
                    Fecha = primero.Fecha, MesOperativo = primero.MesOperativo, Numero = primero.Numero, Tc = primero.Tc,
                    Comprobante = g.Select(x => x.Comprobante).FirstOrDefault(x => x.Length > 0) ?? "",
                    Subdiario = g.Select(x => x.Subdiario).FirstOrDefault(x => x.Length > 0 && x != "ÿRS") ?? "", Unidad = primero.Unidad,
                    Renglones = g.Select(x => new DiarioRenglon(x.Cuenta, cuentas.GetValueOrDefault(x.Cuenta, ""), x.Detalle, x.Debe, x.Haber)).ToList()
                };
            }).OrderBy(a => a.Fecha).ThenBy(a => a.MesOperativo).ThenBy(a => a.Numero).ToList();
        var subs = await SubdiariosAsync(cn, ct);
        return new DiarioResultado
        {
            Filtro = f, Empresa = await EmpresaAsync(cn, ct), Ejercicio = ejercicio.Descripcion, Truncado = truncado,
            SubdiarioDescripcion = sub.Length == 0 ? (f.ExcluirSubdiarios ? "Diario general (sin subdiarios)" : "Todos")
                : sub.Equals("DG", StringComparison.OrdinalIgnoreCase) ? "Diario general" : subs.FirstOrDefault(s => s.Codigo == sub)?.Descripcion ?? sub,
            Asientos = asientos,
            PorCuenta = renglones.GroupBy(r => r.Cuenta, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new DiarioCuentaResumen(g.Key, cuentas.GetValueOrDefault(g.Key, ""), g.Sum(x => x.Debe), g.Sum(x => x.Haber))).ToList(),
            TotalDebe = renglones.Sum(r => r.Debe), TotalHaber = renglones.Sum(r => r.Haber)
        };
    }

    private sealed class RenglonSql
    {
        public bool TieneComprobante { get; set; } public bool EsCabecera { get; set; }
        public DateTime Fecha { get; set; } public int MesOperativo { get; set; } public int Numero { get; set; }
        public string Tc { get; set; } = ""; public string Comprobante { get; set; } = ""; public string Subdiario { get; set; } = ""; public string Unidad { get; set; } = "";
        public string Cuenta { get; set; } = ""; public string Detalle { get; set; } = ""; public decimal Debe { get; set; } public decimal Haber { get; set; }
    }

    // ── Plan de cuentas ─────────────────────────────────────────────────────────────────────────

    public Task<PlanCuentasResultado> GetPlanCuentasAsync(ContabilidadFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(MenuPlanCuentas, "PlanCuentas", cn => LeerPlanAsync(cn, filtro, ct), ct);

    private static async Task<PlanCuentasResultado> LeerPlanAsync(SqlConnection cn, ContabilidadFiltro f, CancellationToken ct)
    {
        var filas = (await cn.QueryAsync<(string Codigo, string Descripcion, bool Titulo, string TipoVista, bool Baja)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)), LTRIM(RTRIM(ISNULL(DESCRIPCION,''))), CONVERT(bit,ISNULL(TITULO,0)), UPPER(LTRIM(RTRIM(ISNULL(TipoVista,'')))), CONVERT(bit,ISNULL(Dada_De_Baja,0))
            FROM dbo.MA_CUENTAS WHERE ISNULL(LTRIM(RTRIM(CODIGO)),'') <> '';
            """, cancellationToken: ct))).ToList();
        var niveles = await NivelesAsync(cn, ct);
        var largos = filas.Select(x => x.Codigo.Length).Distinct().OrderBy(x => x).ToList();
        var texto = f.PlanTexto.Trim();
        var cuentas = filas
            .Where(x => (f.PlanIncluirTerceros || x.TipoVista is not ("CL" or "PR")) && (f.PlanIncluirBaja || !x.Baja))
            .Where(x => texto.Length == 0 || x.Codigo.Contains(texto, StringComparison.OrdinalIgnoreCase) || x.Descripcion.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Codigo, StringComparer.Ordinal)
            .Select(x =>
            {
                var nivel = Math.Max(0, largos.IndexOf(x.Codigo.Length));
                return new PlanCuenta(x.Codigo, x.Descripcion, x.Titulo, nivel, nivel < niveles.Count ? niveles[nivel] : "", x.TipoVista, x.Baja);
            }).ToList();
        return new PlanCuentasResultado { Filtro = f, Empresa = await EmpresaAsync(cn, ct), Cuentas = cuentas };
    }

    private static async Task<List<string>> NivelesAsync(SqlConnection cn, CancellationToken ct)
        => (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition(
            "SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) LIKE N'NIVEL[_][1-9]';",
            cancellationToken: ct))).OrderBy(x => x.Clave).Select(x => x.Valor).ToList();

    // ── Balance general de saldos ───────────────────────────────────────────────────────────────

    public Task<BalanceGeneralResultado> GetBalanceGeneralAsync(ContabilidadFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(MenuBalanceGeneral, "BalanceGeneral", cn => LeerBalanceGeneralAsync(cn, filtro, ct), ct);

    /// <summary>Saldos acumulados del ejercicio a la fecha «hasta» (desde el inicio, con la apertura), por
    /// niveles del plan. Comparativo: el ejercicio anterior a la misma fecha del año anterior.</summary>
    private async Task<BalanceGeneralResultado> LeerBalanceGeneralAsync(SqlConnection cn, ContabilidadFiltro f, CancellationToken ct)
    {
        var actual = await SaldosAcumuladosAsync(cn, f, ct);
        var plan = await PlanAsync(cn, ct);
        var niveles = await NivelesAsync(cn, ct);
        var planT = plan.Select(c => (c.Codigo, c.Descripcion, c.Titulo, c.TipoVista)).ToList();
        var filas = ArmarSumasSaldos(planT, actual.Saldos, agruparTerceros: true, mostrarTitulos: true, sinMovimientos: false);
        Dictionary<string, decimal>? comparativo = null; string? ejComp = null; DateTime? hastaComp = null;
        if (f.Comparativo)
        {
            var anterior = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "SELECT TOP (1) CONVERT(int,PERIODO) FROM dbo.MV_EJERCICIOS WHERE [FECHA HASTA] < @Desde ORDER BY [FECHA HASTA] DESC;",
                new { Desde = actual.Ejercicio.Desde }, cancellationToken: ct));
            if (anterior is { } pAnt)
            {
                var fc = new ContabilidadFiltro { Periodo = pAnt, Desde = DateTime.MinValue, Hasta = f.Hasta.AddYears(-1), Unidades = f.Unidades, IncluirProformas = f.IncluirProformas };
                var ant = await SaldosAcumuladosAsync(cn, fc, ct);
                comparativo = ArmarSumasSaldos(planT, ant.Saldos, true, true, false).GroupBy(x => Clave(x)).ToDictionary(g => g.Key, g => g.First().Saldo);
                ejComp = ant.Ejercicio.Descripcion; hastaComp = fc.Hasta;
                // Las cuentas que solo tienen saldo en el ejercicio anterior también se muestran.
                foreach (var x in ArmarSumasSaldos(planT, ant.Saldos, true, true, false).Where(x => filas.All(y => Clave(y) != Clave(x))))
                    filas.Add(new SumasSaldosFila { Codigo = x.Codigo, Descripcion = x.Descripcion, EsTitulo = x.EsTitulo, Nivel = x.Nivel, Agrupadas = x.Agrupadas });
                filas = filas.OrderBy(r => r.Codigo + (r.EsTitulo ? "" : r.Agrupadas > 0 ? "~" : ""), StringComparer.Ordinal).ToList();
            }
        }
        var resultado = filas
            .Where(x => f.NivelMaximo <= 0 || x.Nivel < f.NivelMaximo)
            .Select(x => new BalanceGeneralFila
            {
                Codigo = x.Codigo, Descripcion = x.Descripcion + (x.Agrupadas > 0 ? $" ({x.Agrupadas})" : ""), EsTitulo = x.EsTitulo, Nivel = x.Nivel,
                NivelNombre = x.Nivel < niveles.Count ? niveles[x.Nivel] : "", Saldo = x.Saldo,
                SaldoComparativo = comparativo is null ? null : comparativo.GetValueOrDefault(Clave(x))
            }).ToList();
        return new BalanceGeneralResultado
        {
            Filtro = f, Empresa = await EmpresaAsync(cn, ct), Ejercicio = actual.Ejercicio.Descripcion, EjercicioComparativo = ejComp, HastaComparativo = hastaComp,
            Filas = resultado, Diferencia = actual.Saldos.Sum(s => s.Anterior + s.Debe - s.Haber)
        };
    }

    private static string Clave(SumasSaldosFila x) => x.Codigo + (x.EsTitulo ? "T" : x.Agrupadas > 0 ? "~" : "");

    private async Task<(ContabilidadEjercicio Ejercicio, List<(string Cuenta, decimal Anterior, decimal Debe, decimal Haber)> Saldos)> SaldosAcumuladosAsync(
        SqlConnection cn, ContabilidadFiltro f, CancellationToken ct)
    {
        // Saldo al «hasta» = todo el ejercicio hasta esa fecha: se toma como período completo (desde el inicio).
        var copia = new ContabilidadFiltro { Periodo = f.Periodo, Desde = DateTime.MinValue, Hasta = f.Hasta, Unidades = f.Unidades, IncluirProformas = f.IncluirProformas };
        var (ejercicio, p, where) = await PrepararAsync(cn, copia, ct);
        if (f.Desde == default || f.Desde < ejercicio.Desde) f.Desde = ejercicio.Desde;
        f.Hasta = copia.Hasta;
        var saldos = (await cn.QueryAsync<(string Cuenta, decimal Anterior, decimal Debe, decimal Haber)>(new CommandDefinition($"""
            SELECT LTRIM(RTRIM(a.CUENTA)), CONVERT(money,0),
                SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE 0 END), SUM(CASE WHEN a.[DEBE-HABER] <> 'D' THEN a.IMPORTE ELSE 0 END)
            FROM dbo.MV_ASIENTOS a WHERE {where}
            GROUP BY LTRIM(RTRIM(a.CUENTA));
            """, p, commandTimeout: 120, cancellationToken: ct))).ToList();
        return (ejercicio, saldos);
    }

    // ── Exportaciones ───────────────────────────────────────────────────────────────────────────

    public Task<ContabilidadArchivo> ExportarDiarioAsync(ContabilidadFiltro filtro, bool subdiarios, bool comoPdf, CancellationToken ct = default)
        => EjecutarAsync(subdiarios ? MenuSubdiarios : MenuDiario, "ExportarDiario", async cn =>
        {
            var r = await LeerDiarioAsync(cn, filtro, subdiarios, ct);
            var titulo = subdiarios ? $"Subdiario {r.SubdiarioDescripcion}" : "Libro diario";
            var nombre = $"{(subdiarios ? "Subdiario_" + r.Filtro.Subdiario : "LibroDiario")}_{r.Filtro.Desde:yyyyMMdd}_{r.Filtro.Hasta:yyyyMMdd}";
            var resumido = subdiarios && r.Filtro.Resumido;
            if (comoPdf) return new ContabilidadArchivo(nombre + ".pdf", "application/pdf", await pdf.GenerateAsync(HtmlDiario(r, titulo, resumido), new DocumentPdfFooterOptions(true, true, r.Empresa), ct));
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(subdiarios ? "Subdiario" : "Diario");
            Encabezado(ws, titulo, r.Empresa, r.Ejercicio, r.Filtro);
            var fila = 5;
            if (resumido)
            {
                string[] t = ["Cuenta", "Descripción", "Debe", "Haber"];
                for (var c = 0; c < t.Length; c++) ws.Cell(4, c + 1).Value = t[c];
                Titulos(ws, 4, t.Length);
                foreach (var x in r.PorCuenta) { ws.Cell(fila, 1).Value = x.Cuenta; ws.Cell(fila, 2).Value = x.Descripcion; ws.Cell(fila, 3).Value = x.Debe; ws.Cell(fila, 4).Value = x.Haber; fila++; }
                ws.Cell(fila, 2).Value = "Totales"; ws.Cell(fila, 3).Value = r.TotalDebe; ws.Cell(fila, 4).Value = r.TotalHaber; ws.Row(fila).Style.Font.Bold = true;
                ws.Range(5, 3, fila, 4).Style.NumberFormat.Format = "#,##0.00";
            }
            else
            {
                string[] t = ["Fecha", "Asiento", "Comprobante", "Cuenta", "Descripción", "Detalle", "Debe", "Haber"];
                for (var c = 0; c < t.Length; c++) ws.Cell(4, c + 1).Value = t[c];
                Titulos(ws, 4, t.Length);
                foreach (var a in r.Asientos)
                {
                    foreach (var x in a.Renglones)
                    {
                        ws.Cell(fila, 1).Value = a.Fecha; ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";
                        ws.Cell(fila, 2).Value = $"{a.MesOperativo}-{a.Numero}"; ws.Cell(fila, 3).Value = $"{a.Tc} {a.Comprobante}".Trim();
                        ws.Cell(fila, 4).Value = x.Cuenta; ws.Cell(fila, 5).Value = x.Descripcion; ws.Cell(fila, 6).Value = x.Detalle;
                        ws.Cell(fila, 7).Value = x.Debe; ws.Cell(fila, 8).Value = x.Haber; fila++;
                    }
                }
                ws.Cell(fila, 6).Value = "Totales"; ws.Cell(fila, 7).Value = r.TotalDebe; ws.Cell(fila, 8).Value = r.TotalHaber; ws.Row(fila).Style.Font.Bold = true;
                ws.Range(5, 7, fila, 8).Style.NumberFormat.Format = "#,##0.00";
            }
            ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(4);
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            return new ContabilidadArchivo(nombre + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray());
        }, ct);

    public Task<ContabilidadArchivo> ExportarPlanCuentasAsync(ContabilidadFiltro filtro, bool comoPdf, CancellationToken ct = default)
        => EjecutarAsync(MenuPlanCuentas, "ExportarPlan", async cn =>
        {
            var r = await LeerPlanAsync(cn, filtro, ct);
            if (comoPdf)
            {
                var sb = new StringBuilder($"<!doctype html><html><head><meta charset=\"utf-8\"><style>{CssPdf}</style></head><body>");
                sb.Append($"<h1>Plan de cuentas</h1><div class=\"sub\">{E(r.Empresa)} · {r.Cuentas.Count} cuentas</div><table><thead><tr><th>Código</th><th>Descripción</th><th>Nivel</th><th>Tipo</th></tr></thead><tbody>");
                foreach (var c in r.Cuentas)
                    sb.Append($"<tr class=\"{(c.Titulo ? "t" : "")}\"><td>{E(c.Codigo)}</td><td style=\"padding-left:{3 + c.Nivel * 10}px\">{E(c.Descripcion)}{(c.Baja ? " (baja)" : "")}</td><td>{E(c.NivelNombre)}</td><td>{(c.Titulo ? "Título" : "Imputable")}</td></tr>");
                return new ContabilidadArchivo("PlanDeCuentas.pdf", "application/pdf", await pdf.GenerateAsync(sb.Append("</tbody></table></body></html>").ToString(), new DocumentPdfFooterOptions(true, true, r.Empresa), ct));
            }
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Plan de cuentas");
            ws.Cell(1, 1).Value = $"Plan de cuentas · {r.Empresa}"; ws.Cell(1, 1).Style.Font.Bold = true;
            string[] t = ["Código", "Descripción", "Nivel", "Tipo", "Vista", "Baja"];
            for (var c = 0; c < t.Length; c++) ws.Cell(3, c + 1).Value = t[c];
            Titulos(ws, 3, t.Length);
            var fila = 4;
            foreach (var c in r.Cuentas)
            {
                ws.Cell(fila, 1).Value = c.Codigo; ws.Cell(fila, 2).Value = new string(' ', c.Nivel * 2) + c.Descripcion; ws.Cell(fila, 3).Value = c.NivelNombre;
                ws.Cell(fila, 4).Value = c.Titulo ? "Título" : "Imputable"; ws.Cell(fila, 5).Value = c.TipoVista; ws.Cell(fila, 6).Value = c.Baja ? "Sí" : "";
                if (c.Titulo) ws.Row(fila).Style.Font.Bold = true;
                fila++;
            }
            ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(3);
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            return new ContabilidadArchivo("PlanDeCuentas.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray());
        }, ct);

    public Task<ContabilidadArchivo> ExportarBalanceGeneralAsync(ContabilidadFiltro filtro, bool comoPdf, CancellationToken ct = default)
        => EjecutarAsync(MenuBalanceGeneral, "ExportarBalanceGeneral", async cn =>
        {
            var r = await LeerBalanceGeneralAsync(cn, filtro, ct);
            var nombre = $"BalanceGeneral_{r.Filtro.Hasta:yyyyMMdd}";
            var comp = r.EjercicioComparativo is not null;
            if (comoPdf)
            {
                var sb = new StringBuilder($"<!doctype html><html><head><meta charset=\"utf-8\"><style>{CssPdf}</style></head><body>");
                sb.Append($"<h1>Balance general de saldos</h1><div class=\"sub\">{E(r.Empresa)} · {E(r.Ejercicio)} · saldos al {r.Filtro.Hasta:dd/MM/yyyy}{(comp ? $" · comparativo {E(r.EjercicioComparativo!)} al {r.HastaComparativo:dd/MM/yyyy}" : "")}</div>");
                sb.Append($"<table><thead><tr><th>Cuenta</th><th>Descripción</th><th class=\"n\">Saldo al {r.Filtro.Hasta:dd/MM/yyyy}</th>{(comp ? $"<th class=\"n\">Saldo al {r.HastaComparativo:dd/MM/yyyy}</th>" : "")}</tr></thead><tbody>");
                foreach (var x in r.Filas)
                    sb.Append($"<tr class=\"{(x.EsTitulo ? "t" : "")}\"><td>{E(x.Codigo)}</td><td style=\"padding-left:{3 + x.Nivel * 10}px\">{E(x.Descripcion)}</td><td class=\"n\">{M(x.Saldo)}</td>{(comp ? $"<td class=\"n\">{M(x.SaldoComparativo ?? 0)}</td>" : "")}</tr>");
                sb.Append("</tbody></table>");
                if (Math.Abs(r.Diferencia) >= 0.005m) sb.Append($"<div class=\"aviso\">Atención: los saldos no suman cero (diferencia {r.Diferencia.ToString("N2", Ar)}): hay asientos desbalanceados. Ver Balance de sumas y saldos.</div>");
                return new ContabilidadArchivo(nombre + ".pdf", "application/pdf", await pdf.GenerateAsync(sb.Append("</body></html>").ToString(), new DocumentPdfFooterOptions(true, true, r.Empresa), ct));
            }
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Balance general");
            Encabezado(ws, "Balance general de saldos", r.Empresa, r.Ejercicio, r.Filtro);
            string[] t = comp ? ["Cuenta", "Descripción", "Nivel", $"Saldo al {r.Filtro.Hasta:dd/MM/yyyy}", $"Saldo al {r.HastaComparativo:dd/MM/yyyy}"] : ["Cuenta", "Descripción", "Nivel", $"Saldo al {r.Filtro.Hasta:dd/MM/yyyy}"];
            for (var c = 0; c < t.Length; c++) ws.Cell(4, c + 1).Value = t[c];
            Titulos(ws, 4, t.Length);
            var fila = 5;
            foreach (var x in r.Filas)
            {
                ws.Cell(fila, 1).Value = x.Codigo; ws.Cell(fila, 2).Value = new string(' ', x.Nivel * 2) + x.Descripcion; ws.Cell(fila, 3).Value = x.NivelNombre;
                ws.Cell(fila, 4).Value = x.Saldo; if (comp) ws.Cell(fila, 5).Value = x.SaldoComparativo ?? 0;
                if (x.EsTitulo) ws.Row(fila).Style.Font.Bold = true;
                fila++;
            }
            ws.Range(5, 4, fila, comp ? 5 : 4).Style.NumberFormat.Format = "#,##0.00";
            ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(4);
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            return new ContabilidadArchivo(nombre + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray());
        }, ct);

    internal static string HtmlDiario(DiarioResultado r, string titulo, bool resumido)
    {
        var sb = new StringBuilder($"<!doctype html><html><head><meta charset=\"utf-8\"><style>{CssPdf}</style></head><body>");
        sb.Append($"<h1>{E(titulo)}</h1><div class=\"sub\">{E(r.Empresa)} · {E(r.Ejercicio)} · {r.Filtro.Desde:dd/MM/yyyy} al {r.Filtro.Hasta:dd/MM/yyyy}</div>");
        if (resumido)
        {
            sb.Append("<table><thead><tr><th>Cuenta</th><th>Descripción</th><th class=\"n\">Debe</th><th class=\"n\">Haber</th></tr></thead><tbody>");
            foreach (var x in r.PorCuenta) sb.Append($"<tr><td>{E(x.Cuenta)}</td><td>{E(x.Descripcion)}</td><td class=\"n\">{M(x.Debe)}</td><td class=\"n\">{M(x.Haber)}</td></tr>");
        }
        else
        {
            sb.Append("<table><thead><tr><th>Cuenta</th><th>Descripción</th><th>Detalle</th><th class=\"n\">Debe</th><th class=\"n\">Haber</th></tr></thead><tbody>");
            foreach (var a in r.Asientos)
            {
                sb.Append($"<tr class=\"cta\"><td colspan=\"5\">{a.Fecha:dd/MM/yyyy} · Asiento {a.MesOperativo}-{a.Numero}{(a.Comprobante.Length > 0 || a.Tc.Length > 0 ? $" · {E(a.Tc)} {E(a.Comprobante)}" : "")}{(a.Subdiario.Length > 0 ? $" · {E(a.Subdiario)}" : "")}</td></tr>");
                foreach (var x in a.Renglones)
                    sb.Append($"<tr><td>{E(x.Cuenta)}</td><td>{E(x.Descripcion)}</td><td>{E(x.Detalle)}</td><td class=\"n\">{M(x.Debe)}</td><td class=\"n\">{M(x.Haber)}</td></tr>");
            }
        }
        sb.Append($"<tr class=\"tot\"><td colspan=\"{(resumido ? 2 : 3)}\">Totales</td><td class=\"n\">{M(r.TotalDebe)}</td><td class=\"n\">{M(r.TotalHaber)}</td></tr></tbody></table>");
        if (r.Truncado) sb.Append($"<div class=\"aviso\">Se muestran los primeros {MaximoRenglonesDiario:N0} renglones: acotá las fechas.</div>");
        return sb.Append("</body></html>").ToString();
    }
}
