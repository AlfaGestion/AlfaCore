using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class CargaViajesViajesExcelExporter
{
    public byte[] Exportar(IReadOnlyList<CargaViajesGridItemDto> rows, CargaViajesFilters filters)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Viajes");

        SetTitulo(ws, "Carga de Viajes", 10);
        SetSubtitulo(ws, BuildSubtitulo(filters, rows.Count), 10);
        SetHeaders(ws, 3, ["Fecha", "Comprobante", "Cliente", "Chofer / Fletero", "Destino", "Tipo vehículo", "Total cliente", "Total fletero", "Estado", "Usuario"]);

        var rowIndex = 4;
        for (var i = 0; i < rows.Count; i++, rowIndex++)
        {
            var item = rows[i];
            ws.Cell(rowIndex, 1).Value = item.Fecha;
            ws.Cell(rowIndex, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(rowIndex, 2).Value = item.IdComprobante;
            ws.Cell(rowIndex, 3).Value = item.ClienteNombre;
            ws.Cell(rowIndex, 4).Value = item.ChoferNombre;
            ws.Cell(rowIndex, 5).Value = item.DestinoDescripcion;
            ws.Cell(rowIndex, 6).Value = item.TipoVehiculoDescripcion;
            ws.Cell(rowIndex, 7).Value = item.TotalCliente;
            ws.Cell(rowIndex, 7).Style.NumberFormat.Format = "$ #,##0.00";
            ws.Cell(rowIndex, 8).Value = item.TotalFletero;
            ws.Cell(rowIndex, 8).Style.NumberFormat.Format = "$ #,##0.00";
            ws.Cell(rowIndex, 9).Value = item.Estado;
            ws.Cell(rowIndex, 10).Value = item.Usuario;

            if (i % 2 == 1)
                SetRowBackground(ws, rowIndex, 10);
        }

        var totalRow = rowIndex;
        ws.Cell(totalRow, 6).Value = "Totales";
        ws.Cell(totalRow, 7).Value = rows.Sum(x => x.TotalCliente);
        ws.Cell(totalRow, 7).Style.NumberFormat.Format = "$ #,##0.00";
        ws.Cell(totalRow, 8).Value = rows.Sum(x => x.TotalFletero);
        ws.Cell(totalRow, 8).Style.NumberFormat.Format = "$ #,##0.00";
        ws.Range(totalRow, 6, totalRow, 8).Style.Font.Bold = true;
        ws.Range(totalRow, 6, totalRow, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#e0f2fe");

        ws.Columns().AdjustToContents(8, 60);
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 24);
        ws.Column(4).Width = Math.Max(ws.Column(4).Width, 24);
        ws.Column(5).Width = Math.Max(ws.Column(5).Width, 24);
        ws.SheetView.FreezeRows(3);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo() =>
        $"carga_viajes_{DateTime.Today:yyyyMMdd}.xlsx";

    private static string BuildSubtitulo(CargaViajesFilters filters, int totalRows)
    {
        var parts = new List<string> { $"{totalRows} viaje(s)" };

        AddFilter(parts, "Texto", filters.Texto);
        AddFilter(parts, "Cliente", filters.Cliente);
        AddFilter(parts, "Chofer", filters.Chofer);
        AddFilter(parts, "Destino", filters.Destino);
        AddFilter(parts, "Tipo vehículo", filters.TipoVehiculo);
        AddFilter(parts, "Estado", filters.Estado);
        AddFilter(parts, "Usuario", filters.Usuario);
        AddFilter(parts, "Comprobante", filters.IdComprobante);

        return $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} · {string.Join(" · ", parts)}";
    }

    private static void AddFilter(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{label}: {value.Trim()}");
    }

    private static void SetTitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(1, 1);
        cell.Value = texto;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 12;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
        cell.Style.Font.FontColor = XLColor.White;
        if (colspan > 1)
            ws.Range(1, 1, 1, colspan).Merge();
    }

    private static void SetSubtitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(2, 1);
        cell.Value = texto;
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Font.FontColor = XLColor.FromHtml("#64748b");
        if (colspan > 1)
            ws.Range(2, 1, 2, colspan).Merge();
    }

    private static void SetHeaders(IXLWorksheet ws, int row, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
        }
    }

    private static void SetRowBackground(IXLWorksheet ws, int row, int cols)
    {
        for (var c = 1; c <= cols; c++)
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
    }
}
