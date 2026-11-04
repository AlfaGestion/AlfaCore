using System.Net;
using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public static class ComprobantesVisorExportador
{
    private static readonly System.Globalization.CultureInfo Ar = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
    private static readonly string[] Columnas = ["Fecha", "TC", "Comprobante", "Cuenta", "Cliente", "Importe", "Anulado", "Unidad", "Usuario"];

    public static byte[] Excel(IReadOnlyList<ComprobantesVisorFila> filas)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Comprobantes");
        for (var c = 0; c < Columnas.Length; c++) hoja.Cell(1, c + 1).Value = Columnas[c];
        hoja.Row(1).Style.Font.Bold = true;
        var f = 2;
        foreach (var x in filas)
        {
            if (x.Fecha is { } fecha) hoja.Cell(f, 1).Value = fecha;
            hoja.Cell(f, 2).Value = x.Tc; hoja.Cell(f, 3).Value = x.Numero;
            hoja.Cell(f, 4).Value = x.Cuenta; hoja.Cell(f, 5).Value = x.Cliente;
            hoja.Cell(f, 6).Value = x.Importe; hoja.Cell(f, 7).Value = x.Anulada ? "Sí" : "No";
            hoja.Cell(f, 8).Value = x.UnidadNegocio; hoja.Cell(f, 9).Value = x.Usuario;
            f++;
        }
        hoja.Column(1).Style.DateFormat.Format = "dd/MM/yyyy";
        hoja.Column(6).Style.NumberFormat.Format = "#,##0.00";
        hoja.Columns().AdjustToContents(1, 60); hoja.SheetView.FreezeRows(1);
        using var archivo = new MemoryStream(); libro.SaveAs(archivo); return archivo.ToArray();
    }

    public static string Html(IReadOnlyList<ComprobantesVisorFila> filas, ComprobantesVisorFiltro filtro)
    {
        var html = new StringBuilder("<!doctype html><html><head><meta charset='utf-8'><style>body{font:10px Arial}table{width:100%;border-collapse:collapse}th,td{padding:5px;border-bottom:1px solid #ddd;text-align:left}thead{display:table-header-group}tr{break-inside:avoid}.n{text-align:right}</style></head><body><h1>Comprobantes de ventas</h1>");
        html.Append($"<p>{filtro.FechaDesde:dd/MM/yyyy} – {filtro.FechaHasta:dd/MM/yyyy} · {filas.Count} comprobantes · {(filtro.IncluirAnulados ? "Incluye anulados" : "Sin anulados")}</p><table><thead><tr>");
        foreach (var titulo in Columnas) html.Append($"<th>{titulo}</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var x in filas)
            html.Append($"<tr><td>{x.Fecha:dd/MM/yyyy}</td><td>{E(x.Tc)}</td><td>{E(x.Numero)}</td><td>{E(x.Cuenta)}</td><td>{E(x.Cliente)}</td><td class='n'>{x.Importe.ToString("N2", Ar)}</td><td>{(x.Anulada ? "Sí" : "No")}</td><td>{E(x.UnidadNegocio)}</td><td>{E(x.Usuario)}</td></tr>");
        return html.Append("</tbody></table></body></html>").ToString();
    }
    private static string E(string valor) => WebUtility.HtmlEncode(valor);
}
