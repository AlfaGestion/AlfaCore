using System.Globalization;
using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IControlArcaService
{
    Task<ControlArcaResultado> ControlarAsync(byte[] archivo, string nombreArchivo, CancellationToken ct = default);
    Task<ContabilidadArchivo> ExportarAsync(ControlArcaResultado resultado, CancellationToken ct = default);
}

/// <summary>
/// Control contra «Mis Comprobantes» de ARCA (como el modo control de EM_ImportacionComprobantes del legacy):
/// emitidos contra las ventas (V_MV_CPTE) y recibidos contra el libro de compras (MV_ASIENTOS LIVA_TIPO=COMPRAS).
/// Solo lectura: no importa ni modifica comprobantes.
/// </summary>
public sealed class ControlArcaService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events) : IControlArcaService
{
    // Permiso: «Importar Comprobantes» (D016023), «Planilla Control de Comprobantes» (D9562) o los reportes contables.
    private static readonly string[] Menus = ["D016023", "D9562", "D957004", "D957005"];
    private const decimal Tolerancia = 1m;
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    // ── Lectura del archivo de ARCA ─────────────────────────────────────────────────────────────

    /// <summary>Lee el Excel (.xlsx) o CSV de Mis Comprobantes (formato con columnas por alícuota y el anterior).</summary>
    internal static ArcaMisComprobantesArchivo Leer(byte[] archivo, string nombre)
    {
        var filas = nombre.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || nombre.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? LeerCsv(archivo) : LeerXlsx(archivo);
        var titulo = filas.Take(3).SelectMany(f => f).FirstOrDefault(c => c.Contains("Mis Comprobantes", StringComparison.OrdinalIgnoreCase)) ?? "";
        var iHeader = filas.FindIndex(f => f.Any(c => Norm(c) == "fecha") && f.Any(c => Norm(c) == "tipo"));
        if (iHeader < 0) throw new ReglaException("El archivo no tiene el formato de «Mis Comprobantes» de ARCA (no se encontró la fila de títulos Fecha / Tipo).");
        var cols = filas[iHeader].Select(Norm).ToList();
        int Col(params string[] nombres) { foreach (var n in nombres) { var i = cols.IndexOf(Norm(n)); if (i >= 0) return i; } return -1; }
        var recibidos = titulo.Contains("Recibidos", StringComparison.OrdinalIgnoreCase) || (titulo.Length == 0 && Col("Denominación Emisor") >= 0);
        var resultado = new ArcaMisComprobantesArchivo { Recibidos = recibidos };
        var mCuit = System.Text.RegularExpressions.Regex.Match(titulo, @"CUIT\s*(\d{11})");
        if (mCuit.Success) resultado.Cuit = mCuit.Groups[1].Value;

        int cFecha = Col("Fecha"), cTipo = Col("Tipo"), cPv = Col("Punto de Venta"), cDesde = Col("Número Desde", "Numero Desde"), cHasta = Col("Número Hasta", "Numero Hasta"),
            cCae = Col("Cód. Autorización", "Cod. Autorizacion", "Código de Autorización"),
            cDoc = recibidos ? Col("Nro. Doc. Emisor") : Col("Nro. Doc. Receptor"),
            cNom = recibidos ? Col("Denominación Emisor") : Col("Denominación Receptor"),
            cDocPropio = recibidos ? Col("Nro. Doc. Receptor") : -1,
            cTc = Col("Tipo Cambio", "Tipo de Cambio"), cMon = Col("Moneda"),
            cNeto = Col("Neto Gravado Total", "Imp. Neto Gravado"), cNoGrav = Col("Neto No Gravado", "Imp. Neto No Gravado"),
            cExento = Col("Op. Exentas", "Imp. Op. Exentas"), cOtros = Col("Otros Tributos"), cIva = Col("Total IVA", "IVA"), cTotal = Col("Imp. Total", "Importe Total");
        if (cFecha < 0 || cTipo < 0 || cPv < 0 || cDesde < 0 || cTotal < 0)
            throw new ReglaException("Al archivo le faltan columnas de «Mis Comprobantes» (Fecha, Tipo, Punto de Venta, Número Desde, Imp. Total).");
        string V(List<string> f, int c) => c >= 0 && c < f.Count ? f[c].Trim() : "";
        foreach (var f in filas.Skip(iHeader + 1))
        {
            if (!DateTime.TryParseExact(V(f, cFecha), ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)) continue;
            var tipo = V(f, cTipo);
            var (codigo, clase, letra) = Tipo(tipo);
            if (clase.Length == 0) continue;
            var c = new ArcaMisComprobante
            {
                Fecha = fecha, CodigoTipo = codigo, TipoTexto = tipo, Clase = clase, Letra = letra,
                PuntoVenta = (int)Num(V(f, cPv)), NumeroDesde = (long)Num(V(f, cDesde)), NumeroHasta = (long)Num(cHasta >= 0 ? V(f, cHasta) : V(f, cDesde)),
                Cae = V(f, cCae), DocContraparte = new string(V(f, cDoc).Where(char.IsDigit).ToArray()), NombreContraparte = V(f, cNom),
                Moneda = cMon >= 0 ? V(f, cMon) : "$", TipoCambio = cTc >= 0 && Num(V(f, cTc)) > 0 ? Num(V(f, cTc)) : 1,
                NetoGravado = Num(V(f, cNeto)), NoGravado = Num(V(f, cNoGrav)), Exento = Num(V(f, cExento)), OtrosTributos = Num(V(f, cOtros)),
                Iva = Num(V(f, cIva)), Total = Num(V(f, cTotal))
            };
            if (c.NumeroHasta < c.NumeroDesde) c.NumeroHasta = c.NumeroDesde;
            if (resultado.Cuit.Length == 0 && cDocPropio >= 0) resultado.Cuit = new string(V(f, cDocPropio).Where(char.IsDigit).ToArray());
            resultado.Comprobantes.Add(c);
        }
        if (resultado.Comprobantes.Count == 0) throw new ReglaException("El archivo no tiene comprobantes.");
        return resultado;
    }

    /// <summary>«1 - Factura A» → (1, FC, A). Por código ARCA y, si no, por el texto.</summary>
    internal static (int Codigo, string Clase, string Letra) Tipo(string texto)
    {
        var partes = texto.Split(" - ", 2);
        _ = int.TryParse(partes[0].Trim(), out var codigo);
        var desc = Norm(partes.Length > 1 ? partes[1] : texto);
        // «Factura de Crédito Electrónica» (FCE) es factura: la nota se reconoce por «nota de crédito/débito».
        var clase = desc.Contains("nota de credito") ? "NC" : desc.Contains("nota de debito") ? "ND" : desc.Contains("recibo") ? "RC"
            : desc.Contains("factura") || desc.Contains("ticket") ? "FC" : "";
        var ultima = desc.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        var letra = ultima.Length == 1 && "abcme".Contains(ultima) ? ultima.ToUpperInvariant() : codigo switch
        {
            >= 1 and <= 5 or >= 201 and <= 205 => "A", >= 6 and <= 10 or >= 206 and <= 210 => "B", >= 11 and <= 15 or >= 211 and <= 215 => "C", >= 51 and <= 54 => "M", _ => ""
        };
        return (codigo, clase, letra);
    }

    private static List<List<string>> LeerXlsx(byte[] archivo)
    {
        try
        {
            using var ms = new MemoryStream(archivo);
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.First();
            var lista = new List<List<string>>();
            var ultimaCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            foreach (var row in ws.RowsUsed())
            {
                var fila = new List<string>();
                for (var c = 1; c <= ultimaCol; c++)
                {
                    var cell = row.Cell(c);
                    fila.Add(cell.DataType switch
                    {
                        XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
                        XLDataType.DateTime => cell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        _ => cell.GetString()
                    });
                }
                lista.Add(fila);
            }
            return lista;
        }
        catch (Exception ex) when (ex is not ReglaException)
        {
            throw new ReglaException("No se pudo leer el Excel. Bajalo de ARCA en formato Excel (.xlsx) o CSV.");
        }
    }

    private static List<List<string>> LeerCsv(byte[] archivo)
    {
        var texto = Encoding.UTF8.GetString(archivo);
        if (texto.Contains('�')) texto = Encoding.Latin1.GetString(archivo);
        var lineas = texto.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sep = lineas.Take(5).Any(l => l.Count(ch => ch == ';') >= 5) ? ';' : ',';
        return lineas.Select(l => SepararCsv(l, sep)).ToList();
    }

    private static List<string> SepararCsv(string linea, char sep)
    {
        var res = new List<string>(); var sb = new StringBuilder(); var comillas = false;
        foreach (var ch in linea)
        {
            if (ch == '"') { comillas = !comillas; continue; }
            if (ch == sep && !comillas) { res.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res;
    }

    /// <summary>Números del Excel (invariantes) o del CSV (con coma decimal).</summary>
    internal static decimal Num(string s)
    {
        s = (s ?? "").Trim().Replace("$", "").Replace(" ", "");
        if (s.Length == 0) return 0;
        if (s.Contains(',') && s.Contains('.')) s = s.LastIndexOf(',') > s.LastIndexOf('.') ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (s.Contains(',')) s = s.Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static string Norm(string s) => new string((s ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('�', ' ').Trim().ToLowerInvariant();

    // ── Comparación ─────────────────────────────────────────────────────────────────────────────

    public Task<ControlArcaResultado> ControlarAsync(byte[] archivo, string nombreArchivo, CancellationToken ct = default)
        => EjecutarAsync("Controlar", async cn =>
        {
            var arca = Leer(archivo, nombreArchivo);
            var desde = arca.Comprobantes.Min(c => c.Fecha); desde = new DateTime(desde.Year, desde.Month, 1);
            var hasta = arca.Comprobantes.Max(c => c.Fecha); hasta = new DateTime(hasta.Year, hasta.Month, 1).AddMonths(1).AddDays(-1);
            var unidades = (await cn.QueryAsync<(string Codigo, string Cuit)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(Codigo)), ISNULL(LTRIM(RTRIM(CUIT)),'') FROM dbo.V_TA_UnidadNegocio;", cancellationToken: ct))).ToList();
            var propias = unidades.Where(u => Digitos(u.Cuit) == arca.Cuit && arca.Cuit.Length > 0).Select(u => u.Codigo).ToList();
            var cuitEmpresa = Digitos(await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT TOP (1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'WSFE_CUIT', N'CUIT') ORDER BY CASE WHEN UPPER(CLAVE)='WSFE_CUIT' THEN 0 ELSE 1 END;",
                cancellationToken: ct)) ?? "");
            string? aviso = null;
            if (propias.Count == 0 && arca.Cuit.Length > 0 && arca.Cuit != cuitEmpresa)
                aviso = $"El archivo es del CUIT {arca.Cuit}, que no es el de ninguna unidad de negocio ni el de la empresa: se compara contra todos los comprobantes.";
            // Ventana amplia para encontrar comprobantes del archivo cargados en otro período.
            var p = new { Desde = desde.AddDays(-62), Hasta = hasta.AddDays(62), Unidades = propias.ToArray(), SinUnidad = propias.Count == 0 };
            var sistema = arca.Recibidos
                ? (await cn.QueryAsync<SistemaSql>(new CommandDefinition("""
                    SELECT LTRIM(RTRIM(ISNULL(a.TC,''))) Tc, LTRIM(RTRIM(ISNULL(a.SUCURSAL,''))) Sucursal, LTRIM(RTRIM(ISNULL(a.NUMERO,''))) Numero, LTRIM(RTRIM(ISNULL(a.LETRA,''))) Letra,
                           a.FECHA Fecha, ABS(ISNULL(a.LIVA_TOTAL,0)) Total, ISNULL(a.CABCUIT,'') Doc, LTRIM(RTRIM(ISNULL(a.CABNOMBRE,''))) Nombre
                    FROM dbo.MV_ASIENTOS a
                    WHERE a.LIVA_TIPO = N'COMPRAS' AND a.FECHA >= @Desde AND a.FECHA < DATEADD(day,1,@Hasta)
                      AND (@SinUnidad = 1 OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) IN @Unidades);
                    """, p, commandTimeout: 120, cancellationToken: ct))).ToList()
                : (await cn.QueryAsync<SistemaSql>(new CommandDefinition("""
                    SELECT LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(ISNULL(c.SUCURSAL,''))) Sucursal, LTRIM(RTRIM(ISNULL(c.NUMERO,''))) Numero, LTRIM(RTRIM(ISNULL(c.LETRA,''))) Letra,
                           c.FECHA Fecha, ISNULL(c.IMPORTE,0) Total, ISNULL(c.DOCUMENTONUMERO,'') Doc, LTRIM(RTRIM(ISNULL(c.NOMBRE,''))) Nombre
                    FROM dbo.V_MV_CPTE c
                    WHERE LTRIM(RTRIM(c.TC)) IN ('FC','NC','ND') AND ISNULL(c.IDCOMPLEMENTO,0) = 0 AND ISNULL(c.ANULADA,0) = 0
                      AND LTRIM(RTRIM(c.LETRA)) IN ('A','B','C','M') AND c.FECHA >= @Desde AND c.FECHA < DATEADD(day,1,@Hasta)
                      AND (@SinUnidad = 1 OR LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) IN @Unidades);
                    """, p, commandTimeout: 120, cancellationToken: ct))).ToList();
            var resultado = Comparar(arca, sistema.Select(s => new ComprobanteSistema(
                ClaseSistema(s.Tc, arca.Recibidos), s.Letra, (int)Num(s.Sucursal), (long)Num(s.Numero), s.Fecha.Date, s.Total, Digitos(s.Doc), s.Nombre,
                $"{s.Tc} {s.Sucursal}-{s.Numero} {s.Letra}".Trim())).ToList(), desde, hasta);
            resultado.CuitArchivo = arca.Cuit; resultado.Unidades = propias; resultado.Aviso = aviso;
            resultado.Emisor = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT TOP (1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'NOMBRE';", cancellationToken: ct)) ?? "";
            await events.LogAuditAsync("ControlArca", "Controlar", "MIS_COMPROBANTES", nombreArchivo,
                $"Controló {resultado.ComprobantesArca} comprobantes {(arca.Recibidos ? "recibidos" : "emitidos")} de ARCA ({desde:MM/yyyy}–{hasta:MM/yyyy}).",
                new { arca.Cuit, Coinciden = resultado.Cantidad(ControlArcaEstado.Coincide), Faltan = resultado.Cantidad(ControlArcaEstado.FaltaEnSistema) }, CancellationToken.None);
            return resultado;
        }, ct);

    internal sealed record ComprobanteSistema(string Clase, string Letra, int PuntoVenta, long Numero, DateTime Fecha, decimal Total, string Doc, string Nombre, string Referencia);

    private static string ClaseSistema(string tc, bool compras) => tc.Trim().ToUpperInvariant() switch
    {
        "FC" or "FCC" or "" => "FC", "NC" or "NCC" => "NC", "ND" or "NDC" => "ND", "RC" => "RC", var otro => otro
    };

    private static string Digitos(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Empareja por clase, letra, punto de venta y número (y CUIT del proveedor en recibidos).
    /// Los rangos «desde/hasta» de ARCA (comprobantes B/C agrupados) se comparan contra la suma del rango.</summary>
    internal static ControlArcaResultado Comparar(ArcaMisComprobantesArchivo arca, IReadOnlyList<ComprobanteSistema> sistema, DateTime desde, DateTime hasta)
    {
        var usados = new HashSet<ComprobanteSistema>();
        var filas = new List<ControlArcaFila>();
        bool MismaContraparte(ComprobanteSistema s, ArcaMisComprobante a) => !arca.Recibidos || s.Doc.Length == 0 || a.DocContraparte.Length == 0 || s.Doc == a.DocContraparte;
        foreach (var a in arca.Comprobantes.OrderBy(x => x.Fecha).ThenBy(x => x.PuntoVenta).ThenBy(x => x.NumeroDesde))
        {
            var candidatos = sistema.Where(s => !usados.Contains(s) && s.Clase == a.Clase && s.PuntoVenta == a.PuntoVenta
                && s.Numero >= a.NumeroDesde && s.Numero <= a.NumeroHasta && (s.Letra == a.Letra || a.Letra.Length == 0) && MismaContraparte(s, a)).ToList();
            // En recibidos puede haber dos proveedores con el mismo punto y número: si hay CUIT, ya filtró.
            var fila = new ControlArcaFila
            {
                Clase = a.Clase, Letra = a.Letra, PuntoVenta = a.PuntoVenta, Numero = a.NumeroDesde, NumeroHasta = a.NumeroHasta,
                Cuit = a.DocContraparte, Nombre = a.NombreContraparte, FechaArca = a.Fecha, TotalArca = a.TotalPesos, Cae = a.Cae
            };
            if (candidatos.Count == 0)
            {
                fila.Estado = ControlArcaEstado.FaltaEnSistema;
                fila.Observacion = arca.Recibidos ? "No está cargado en compras (o se cargó con otro número o proveedor)." : "No está en las ventas del sistema.";
            }
            else
            {
                foreach (var s in candidatos) usados.Add(s);
                fila.TotalSistema = candidatos.Sum(s => s.Total);
                fila.FechaSistema = candidatos[0].Fecha;
                fila.ComprobanteSistema = candidatos.Count == 1 ? candidatos[0].Referencia : $"{candidatos.Count} comprobantes";
                if (fila.Nombre.Length == 0) fila.Nombre = candidatos[0].Nombre;
                var notas = new List<string>();
                if (!arca.Recibidos && candidatos[0].Doc.Length > 0 && a.DocContraparte.Length > 0 && candidatos[0].Doc != a.DocContraparte && a.DocContraparte.TrimStart('0').Length > 0)
                    notas.Add($"Documento distinto en el sistema ({candidatos[0].Doc})");
                if (Math.Abs(fila.Diferencia) > Tolerancia)
                    fila.Estado = ControlArcaEstado.DifiereImporte;
                else if (fila.FechaSistema.Value.Year != a.Fecha.Year || fila.FechaSistema.Value.Month != a.Fecha.Month)
                {
                    fila.Estado = ControlArcaEstado.OtroPeriodo;
                    notas.Add($"En el sistema está en {fila.FechaSistema:MM/yyyy}");
                }
                else if (fila.FechaSistema.Value != a.Fecha) fila.Estado = ControlArcaEstado.DifiereFecha;
                else fila.Estado = ControlArcaEstado.Coincide;
                fila.Observacion = string.Join(" · ", notas);
            }
            filas.Add(fila);
        }
        // Del sistema, dentro del período del archivo y sin pareja en ARCA.
        foreach (var s in sistema.Where(s => !usados.Contains(s) && s.Fecha >= desde && s.Fecha <= hasta && (s.Clase is "FC" or "NC" or "ND")))
            filas.Add(new ControlArcaFila
            {
                Estado = ControlArcaEstado.FaltaEnArca, Clase = s.Clase, Letra = s.Letra, PuntoVenta = s.PuntoVenta, Numero = s.Numero, NumeroHasta = s.Numero,
                Cuit = s.Doc, Nombre = s.Nombre, FechaSistema = s.Fecha, TotalSistema = s.Total, ComprobanteSistema = s.Referencia,
                Observacion = arca.Recibidos ? "Cargado en compras pero ARCA no lo informa (revisá número, CUIT o si es un comprobante no electrónico)." : "En el sistema pero ARCA no lo informa (¿sin CAE o de otro CUIT?)."
            });
        return new ControlArcaResultado
        {
            Recibidos = arca.Recibidos, Desde = desde, Hasta = hasta,
            Filas = filas.OrderBy(f => f.Estado == ControlArcaEstado.Coincide).ThenBy(f => f.Estado).ThenBy(f => f.FechaArca ?? f.FechaSistema).ThenBy(f => f.PuntoVenta).ThenBy(f => f.Numero).ToList(),
            ComprobantesArca = arca.Comprobantes.Count, ComprobantesSistema = sistema.Count(s => s.Fecha >= desde && s.Fecha <= hasta),
            TotalArca = arca.Comprobantes.Sum(c => c.Clase == "NC" ? -c.TotalPesos : c.TotalPesos),
            TotalSistema = sistema.Where(s => s.Fecha >= desde && s.Fecha <= hasta).Sum(s => s.Clase == "NC" ? -s.Total : s.Total)
        };
    }

    public Task<ContabilidadArchivo> ExportarAsync(ControlArcaResultado r, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Control ARCA");
        ws.Cell(1, 1).Value = $"Control contra ARCA · Mis Comprobantes {(r.Recibidos ? "Recibidos" : "Emitidos")} · CUIT {r.CuitArchivo} · {r.Desde:dd/MM/yyyy} al {r.Hasta:dd/MM/yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        string[] t = ["Estado", "Tipo", "Comprobante", "CUIT", "Nombre", "Fecha ARCA", "Fecha sistema", "Total ARCA", "Total sistema", "Diferencia", "En el sistema", "CAE", "Observación"];
        for (var c = 0; c < t.Length; c++) ws.Cell(3, c + 1).Value = t[c];
        ws.Range(3, 1, 3, t.Length).Style.Font.Bold = true;
        ws.Range(3, 1, 3, t.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8ECF2");
        var fila = 4;
        foreach (var f in r.Filas)
        {
            ws.Cell(fila, 1).Value = EstadoTexto(f.Estado); ws.Cell(fila, 2).Value = $"{f.Clase} {f.Letra}"; ws.Cell(fila, 3).Value = f.Comprobante;
            ws.Cell(fila, 4).Value = f.Cuit; ws.Cell(fila, 5).Value = f.Nombre;
            if (f.FechaArca is { } fa) { ws.Cell(fila, 6).Value = fa; ws.Cell(fila, 6).Style.DateFormat.Format = "dd/MM/yyyy"; }
            if (f.FechaSistema is { } fs) { ws.Cell(fila, 7).Value = fs; ws.Cell(fila, 7).Style.DateFormat.Format = "dd/MM/yyyy"; }
            if (f.TotalArca is { } ta) ws.Cell(fila, 8).Value = ta;
            if (f.TotalSistema is { } ts) ws.Cell(fila, 9).Value = ts;
            ws.Cell(fila, 10).Value = f.Diferencia; ws.Cell(fila, 11).Value = f.ComprobanteSistema; ws.Cell(fila, 12).Value = f.Cae; ws.Cell(fila, 13).Value = f.Observacion;
            fila++;
        }
        ws.Range(4, 8, fila, 10).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents(); ws.Column(13).Width = Math.Min(ws.Column(13).Width, 70); ws.SheetView.FreezeRows(3);
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return Task.FromResult(new ContabilidadArchivo($"ControlARCA_{(r.Recibidos ? "Recibidos" : "Emitidos")}_{r.Desde:yyyyMM}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray()));
    }

    public static string EstadoTexto(ControlArcaEstado e) => e switch
    {
        ControlArcaEstado.Coincide => "Coincide",
        ControlArcaEstado.DifiereImporte => "Difiere el importe",
        ControlArcaEstado.DifiereFecha => "Difiere la fecha",
        ControlArcaEstado.OtroPeriodo => "Otro período",
        ControlArcaEstado.FaltaEnSistema => "Falta en el sistema",
        _ => "No está en ARCA"
    };

    private sealed class SistemaSql
    {
        public string Tc { get; set; } = ""; public string Sucursal { get; set; } = ""; public string Numero { get; set; } = ""; public string Letra { get; set; } = "";
        public DateTime Fecha { get; set; } public decimal Total { get; set; } public string Doc { get; set; } = ""; public string Nombre { get; set; } = "";
    }

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _))
            throw new ReglaException("Ingresá en la base activa.");
        try
        {
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            var permitido = false;
            foreach (var m in Menus) if (await permissions.HasAccessAsync(m, ct)) { permitido = true; break; }
            if (!permitido) throw new ReglaException("Tu usuario no tiene permiso para el control contra ARCA (Importar Comprobantes o reportes contables).");
            return await operacion(cn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("ControlArca", accion, ex, "No se pudo hacer el control contra ARCA.", ct: ct);
            throw new AppUserFacingException("No se pudo hacer el control contra ARCA.", codigo, ex);
        }
    }
}
