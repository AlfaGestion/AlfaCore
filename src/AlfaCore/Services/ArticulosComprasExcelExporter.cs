using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class ArticulosComprasExcelExporter
{
    public byte[] Exportar(IReadOnlyList<ArticuloResumenDto> rows, DashboardFilters filters, string texto, string variacion)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Artículos");

        SetTitulo(ws, "Artículos de compras", 13);
        SetSubtitulo(ws, BuildSubtitulo(filters, rows.Count, texto, variacion), 13);
        SetHeaders(ws, 3,
        [
            "Código",
            "Descripción",
            "Fecha alta",
            "Cantidad",
            "Total",
            "Costo prom.",
            "Precio ant.",
            "Precio act.",
            "Variación",
            "Última compra",
            "Proveedor principal",
            "% proveedor",
            "Compras"
        ]);

        var rowIndex = 4;
        for (var i = 0; i < rows.Count; i++, rowIndex++)
        {
            var item = rows[i];
            ws.Cell(rowIndex, 1).Value = item.IdArticulo;
            ws.Cell(rowIndex, 2).Value = item.DescripcionArticulo;
            SetDateCell(ws.Cell(rowIndex, 3), item.FechaAlta);
            SetDecimalCell(ws.Cell(rowIndex, 4), item.CantidadComprada, "#,##0.00");
            SetDecimalCell(ws.Cell(rowIndex, 5), item.TotalComprado, "$ #,##0.00");
            SetDecimalCell(ws.Cell(rowIndex, 6), item.CostoPromedio, "$ #,##0.00");
            SetDecimalCell(ws.Cell(rowIndex, 7), item.PrecioAnterior, "$ #,##0.00");
            SetDecimalCell(ws.Cell(rowIndex, 8), item.PrecioActual, "$ #,##0.00");
            ws.Cell(rowIndex, 9).Value = item.VariacionPrecio;
            ws.Cell(rowIndex, 9).Style.NumberFormat.Format = "0.00%";
            SetDateCell(ws.Cell(rowIndex, 10), item.UltimaCompra);
            ws.Cell(rowIndex, 11).Value = item.ProveedorPrincipal;
            ws.Cell(rowIndex, 12).Value = item.ParticipacionProveedorPrincipal;
            ws.Cell(rowIndex, 12).Style.NumberFormat.Format = "0.00%";
            ws.Cell(rowIndex, 13).Value = item.CantidadCompras;

            if (i % 2 == 1)
                SetRowBackground(ws, rowIndex, 13);
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 32);
        ws.Column(11).Width = Math.Max(ws.Column(11).Width, 28);
        ws.SheetView.FreezeRows(3);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo()
        => $"compras_articulos_{DateTime.Today:yyyyMMdd}.xlsx";

    private static string BuildSubtitulo(DashboardFilters filters, int totalRows, string texto, string variacion)
    {
        var parts = new List<string> { $"{totalRows} artículo(s)" };
        AddFilter(parts, "Desde", filters.FechaDesde?.ToString("dd/MM/yyyy"));
        AddFilter(parts, "Hasta", filters.FechaHasta?.ToString("dd/MM/yyyy"));
        AddFilter(parts, "Proveedor", filters.Proveedor);
        AddFilter(parts, "Artículo", filters.Articulo);
        AddFilter(parts, "Rubro", filters.Rubro);
        AddFilter(parts, "Familia", filters.Familia);
        AddFilter(parts, "Usuario", filters.Usuario);
        AddFilter(parts, "Sucursal", filters.Sucursal);
        AddFilter(parts, "Depósito", filters.Deposito);
        AddFilter(parts, "TC", filters.TipoComprobante);
        AddFilter(parts, "Buscar", texto);
        AddFilter(parts, "Vista", GetVariacionLabel(variacion));

        return $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} · {string.Join(" · ", parts)}";
    }

    private static void AddFilter(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{label}: {value.Trim()}");
    }

    private static string GetVariacionLabel(string variacion)
        => variacion switch
        {
            "subio" => "Subieron",
            "bajo" => "Bajaron",
            "plano" => "Sin variación",
            "nuevo" => "Nuevos",
            "alta" => "Altas",
            _ => string.Empty
        };

    private static void SetTitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(1, 1);
        cell.Value = texto;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 12;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
        cell.Style.Font.FontColor = XLColor.White;
        ws.Range(1, 1, 1, colspan).Merge();
    }

    private static void SetSubtitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(2, 1);
        cell.Value = texto;
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Font.FontColor = XLColor.FromHtml("#64748b");
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

    private static void SetDateCell(IXLCell cell, DateTime? value)
    {
        if (!value.HasValue)
            return;

        cell.Value = value.Value;
        cell.Style.DateFormat.Format = "dd/MM/yyyy";
    }

    private static void SetDecimalCell(IXLCell cell, decimal value, string format)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = format;
    }

    private static void SetRowBackground(IXLWorksheet ws, int row, int cols)
    {
        for (var c = 1; c <= cols; c++)
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
    }
}
