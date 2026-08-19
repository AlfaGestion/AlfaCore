using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

// Exportación Excel de la Lista de precios del Portal Cliente. Nunca incluye costos ni márgenes
// internos: únicamente las columnas que el cliente ya ve en pantalla (Código, Descripción,
// Presentación, Marca, Familia, Rubro, Precio), respetando la búsqueda y la agrupación aplicadas.
public sealed class ListaPreciosClienteExcelExporter
{
    private static readonly string[] Headers = ["Código", "Descripción", "Presentación", "Marca", "Familia", "Rubro", "Precio"];

    public byte[] Exportar(
        IReadOnlyList<ListaPreciosArticuloDto> articulos,
        ListaPreciosResolucionDto resolucion,
        string nombreEmpresa,
        string nombreCliente,
        string? texto,
        string agruparPor,
        bool truncado = false)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Lista de precios");

        SetTitulo(ws, $"Lista de precios — {nombreEmpresa}");
        SetSubtitulo(ws, BuildSubtitulo(resolucion, nombreCliente, articulos.Count, texto, agruparPor));

        var row = 4;

        if (truncado)
        {
            SetAviso(ws, row, $"Se alcanzó el máximo de {articulos.Count:N0} artículos exportables de una vez. Refiná la búsqueda o los filtros para exportar el resto.");
            row++;
        }

        if (agruparPor is ListaPreciosAgruparPorKeys.Familia or ListaPreciosAgruparPorKeys.Marca)
        {
            var grupos = agruparPor == ListaPreciosAgruparPorKeys.Familia
                ? articulos.GroupBy(a => string.IsNullOrWhiteSpace(a.Familia) ? "Sin familia" : a.Familia.Trim())
                : articulos.GroupBy(a => string.IsNullOrWhiteSpace(a.Marca) ? "Sin marca" : a.Marca.Trim());

            foreach (var grupo in grupos.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                SetGrupoHeader(ws, row, grupo.Key.ToUpperInvariant());
                row++;

                SetHeaders(ws, row);
                row++;

                row = EscribirFilas(ws, grupo.ToList(), row);
            }
        }
        else
        {
            SetHeaders(ws, row);
            row++;
            row = EscribirFilas(ws, articulos, row);
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 32);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo()
        => $"lista_precios_{DateTime.Today:yyyyMMdd}.xlsx";

    private static int EscribirFilas(IXLWorksheet ws, IReadOnlyList<ListaPreciosArticuloDto> articulos, int startRow)
    {
        var row = startRow;
        for (var i = 0; i < articulos.Count; i++, row++)
        {
            var item = articulos[i];
            ws.Cell(row, 1).Value = item.IdArticulo;
            ws.Cell(row, 2).Value = item.Descripcion;
            ws.Cell(row, 3).Value = item.Presentacion;
            ws.Cell(row, 4).Value = item.Marca;
            ws.Cell(row, 5).Value = item.Familia;
            ws.Cell(row, 6).Value = item.Rubro;

            var precioCell = ws.Cell(row, 7);
            if (item.SinPrecio)
                precioCell.Value = "Consultar";
            else
            {
                precioCell.Value = item.Precio;
                precioCell.Style.NumberFormat.Format = "$ #,##0.00";
            }

            if (i % 2 == 1)
                SetRowBackground(ws, row);
        }

        return row;
    }

    private static string BuildSubtitulo(ListaPreciosResolucionDto resolucion, string nombreCliente, int totalRows, string? texto, string agruparPor)
    {
        var parts = new List<string> { $"{totalRows} artículo(s)" };

        if (!string.IsNullOrWhiteSpace(nombreCliente))
            parts.Add($"Cliente: {nombreCliente}");

        parts.Add(resolucion.UsaMaestro
            ? "Lista: precio de maestro (sin lista)"
            : $"Lista: {(string.IsNullOrWhiteSpace(resolucion.NombreLista) ? resolucion.IdLista : resolucion.NombreLista)}");
        parts.Add($"Clase de precio: {resolucion.Clase}");

        if (!string.IsNullOrWhiteSpace(texto))
            parts.Add($"Buscar: {texto.Trim()}");

        if (agruparPor is ListaPreciosAgruparPorKeys.Familia)
            parts.Add("Agrupado por Familia");
        else if (agruparPor is ListaPreciosAgruparPorKeys.Marca)
            parts.Add("Agrupado por Marca");

        return $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} · {string.Join(" · ", parts)}";
    }

    private static void SetTitulo(IXLWorksheet ws, string texto)
    {
        var cell = ws.Cell(1, 1);
        cell.Value = texto;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 12;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
        cell.Style.Font.FontColor = XLColor.White;
        ws.Range(1, 1, 1, Headers.Length).Merge();
    }

    private static void SetSubtitulo(IXLWorksheet ws, string texto)
    {
        var cell = ws.Cell(2, 1);
        cell.Value = texto;
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Font.FontColor = XLColor.FromHtml("#64748b");
        ws.Range(2, 1, 2, Headers.Length).Merge();
    }

    private static void SetAviso(IXLWorksheet ws, int row, string texto)
    {
        var cell = ws.Cell(row, 1);
        cell.Value = $"⚠ {texto}";
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef3c7");
        cell.Style.Font.FontColor = XLColor.FromHtml("#92400e");
        ws.Range(row, 1, row, Headers.Length).Merge();
    }

    private static void SetGrupoHeader(IXLWorksheet ws, int row, string texto)
    {
        var cell = ws.Cell(row, 1);
        cell.Value = texto;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 10;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
        cell.Style.Font.FontColor = XLColor.FromHtml("#0f172a");
        ws.Range(row, 1, row, Headers.Length).Merge();
    }

    private static void SetHeaders(IXLWorksheet ws, int row)
    {
        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
        }
    }

    private static void SetRowBackground(IXLWorksheet ws, int row)
    {
        for (var c = 1; c <= Headers.Length; c++)
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
    }
}
