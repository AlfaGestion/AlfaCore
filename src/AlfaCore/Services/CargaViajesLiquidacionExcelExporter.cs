using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class CargaViajesLiquidacionExcelExporter
{
    private static readonly System.Globalization.CultureInfo Cultura =
        System.Globalization.CultureInfo.GetCultureInfo("es-AR");

    public byte[] Exportar(
        IReadOnlyList<CargaViajeReporteLiquidacionRowDto> rows,
        CargaViajesReporteLiquidacionFilters filters,
        string tituloEntidad,
        string nombreEntidad)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Liquidación");

        SetTitulo(ws, "Liquidación de Choferes / Fleteros", 6);
        SetSubtitulo(ws, BuildSubtitulo(filters, nombreEntidad), 6);

        ws.Cell(3, 1).Value = "Entidad";
        ws.Cell(3, 2).Value = tituloEntidad;
        ws.Cell(3, 3).Value = "Viajes";
        ws.Cell(3, 4).Value = rows.Sum(x => Math.Max(1, x.CantidadViajes));
        ws.Cell(3, 5).Value = "Subtotal peajes";
        ws.Cell(3, 6).Value = rows.Sum(x => Math.Max(0m, x.Peaje));
        ws.Cell(4, 1).Value = "Subtotal viajes";
        ws.Cell(4, 2).Value = rows.Sum(x => x.TotalFlete);
        ws.Cell(4, 3).Value = "TOTAL";
        ws.Cell(4, 4).Value = rows.Sum(x => x.TotalConPeaje);
        ws.Cell(4, 5).Value = string.Empty;
        ws.Cell(4, 6).Value = string.Empty;

        for (var c = 1; c <= 6; c++)
        {
            ws.Cell(3, c).Style.Font.Bold = true;
            ws.Cell(4, c).Style.Font.Bold = true;
        }

        ws.Range(4, 1, 4, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#eff6ff");
        ws.Range(4, 3, 4, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
        ws.Cell(4, 3).Style.Font.FontColor = XLColor.FromHtml("#1d4ed8");
        ws.Cell(4, 4).Style.Font.FontColor = XLColor.FromHtml("#1d4ed8");

        var headers = new[] { "Fecha", "Destino", "Cantidad", "Importe", "Peajes", "Observaciones" };
        SetHeaders(ws, 6, headers);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var excelRow = i + 7;

            SetDateCell(ws.Cell(excelRow, 1), row.Fecha);
            ws.Cell(excelRow, 2).Value = BuildDestino(row);
            ws.Cell(excelRow, 3).Value = Math.Max(1, row.CantidadViajes);
            SetMoneyCell(ws.Cell(excelRow, 4), row.TotalFlete);
            SetMoneyCell(ws.Cell(excelRow, 5), Math.Max(0m, row.Peaje));
            ws.Cell(excelRow, 6).Value = row.Observaciones;

            if (i % 2 == 1)
                SetRowBackground(ws, excelRow, 6);
        }

        var totalRow = rows.Count + 8;
        ws.Cell(totalRow, 1).Value = "Subtotal viajes";
        ws.Cell(totalRow, 2).Value = rows.Sum(x => x.TotalFlete);
        ws.Cell(totalRow + 1, 1).Value = "Subtotal peajes";
        ws.Cell(totalRow + 1, 2).Value = rows.Sum(x => Math.Max(0m, x.Peaje));
        ws.Cell(totalRow + 2, 1).Value = "TOTAL";
        ws.Cell(totalRow + 2, 2).Value = rows.Sum(x => x.TotalConPeaje);

        foreach (var row in new[] { totalRow, totalRow + 1, totalRow + 2 })
        {
            ws.Range(row, 1, row, 2).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        ws.Cell(totalRow + 2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
        ws.Cell(totalRow + 2, 1).Style.Font.FontColor = XLColor.White;
        ws.Cell(totalRow + 2, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
        ws.Cell(totalRow + 2, 2).Style.Font.FontColor = XLColor.FromHtml("#1d4ed8");

        ws.Columns().AdjustToContents(8, 60);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 36);
        ws.Column(6).Width = Math.Max(ws.Column(6).Width, 36);
        ws.SheetView.FreezeRows(6);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo() =>
        $"liquidacion_viajes_{DateTime.Today:yyyyMMdd}.xlsx";

    private static string BuildSubtitulo(CargaViajesReporteLiquidacionFilters filters, string nombreEntidad)
    {
        var desde = filters.FechaDesde?.ToString("dd/MM/yyyy") ?? "—";
        var hasta = filters.FechaHasta?.ToString("dd/MM/yyyy") ?? "—";
        return $"{nombreEntidad} · Período {desde} al {hasta}";
    }

    private static string BuildDestino(CargaViajeReporteLiquidacionRowDto row)
    {
        var cliente = string.IsNullOrWhiteSpace(row.ClienteNombre) ? "-" : row.ClienteNombre.Trim();
        var destino = string.IsNullOrWhiteSpace(row.DestinoDescripcion) ? "-" : row.DestinoDescripcion.Trim();

        if (cliente != "-" && destino != "-")
            return $"{cliente} → {destino}";

        return cliente != "-" ? cliente : destino;
    }

    private static void SetTitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(1, 1);
        cell.Value = texto;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 13;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
        if (colspan > 1)
            ws.Range(1, 1, 1, colspan).Merge();
    }

    private static void SetSubtitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(2, 1);
        cell.Value = $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} · {texto}";
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

    private static void SetMoneyCell(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = "$ #,##0";
    }

    private static void SetDateCell(IXLCell cell, DateTime value)
    {
        cell.Value = value;
        cell.Style.DateFormat.Format = "dd/MM/yyyy";
    }

    private static void SetRowBackground(IXLWorksheet ws, int row, int cols)
    {
        for (var c = 1; c <= cols; c++)
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
    }
}
