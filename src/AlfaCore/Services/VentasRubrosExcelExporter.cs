using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class VentasRubrosExcelExporter
{
    public byte[] Exportar(VentasRubrosPageDto page, VentasDashboardFilters filters)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Rubros");
        var comparativo = filters.UnidadesNegocio.Count > 1;
        var headers = BuildHeaders(page, comparativo);

        SetTitulo(ws, "Rubros de ventas", headers.Count);
        SetSubtitulo(ws, BuildSubtitulo(filters, page.Rubros.Count, comparativo), headers.Count);
        SetHeaders(ws, 3, headers);

        var rowIndex = 4;
        foreach (var item in page.Rubros)
        {
            var col = 1;
            ws.Cell(rowIndex, col++).Value = RubroTexto(item.Rubro);

            if (comparativo)
            {
                foreach (var unidad in page.UnidadesNegocio)
                {
                    var detalle = item.UnidadesNegocio.FirstOrDefault(x => x.Codigo == unidad.Codigo);
                    SetDecimalCell(ws.Cell(rowIndex, col++), detalle?.TotalVendido ?? 0, "$ #,##0.00");
                    SetPercentCell(ws.Cell(rowIndex, col++), detalle?.Participacion ?? 0);
                }

                SetDecimalCell(ws.Cell(rowIndex, col++), item.TotalVendido, "$ #,##0.00");
                SetPercentCell(ws.Cell(rowIndex, col++), item.Participacion);
            }
            else
            {
                SetDecimalCell(ws.Cell(rowIndex, col++), item.TotalVendido, "$ #,##0.00");
                SetPercentCell(ws.Cell(rowIndex, col++), item.Participacion);
                ws.Cell(rowIndex, col++).Value = item.CantidadArticulos;
                ws.Cell(rowIndex, col++).Value = item.CantidadComprobantes;
            }

            if ((rowIndex - 4) % 2 == 1)
                SetRowBackground(ws, rowIndex, headers.Count);

            rowIndex++;
        }

        if (comparativo && page.Rubros.Count > 0)
        {
            var col = 1;
            ws.Cell(rowIndex, col++).Value = "Total";
            ws.Row(rowIndex).Style.Font.Bold = true;

            foreach (var unidad in page.UnidadesNegocio)
            {
                var totalUnidad = page.Rubros.Sum(r => r.UnidadesNegocio.FirstOrDefault(x => x.Codigo == unidad.Codigo)?.TotalVendido ?? 0);
                SetDecimalCell(ws.Cell(rowIndex, col++), totalUnidad, "$ #,##0.00");
                SetPercentCell(ws.Cell(rowIndex, col++), page.TotalVendido == 0 ? 0 : totalUnidad / page.TotalVendido);
            }

            SetDecimalCell(ws.Cell(rowIndex, col++), page.TotalVendido, "$ #,##0.00");
            SetPercentCell(ws.Cell(rowIndex, col++), 1);
            SetRowBackground(ws, rowIndex, headers.Count, "#dbeafe");
        }

        ws.Columns().AdjustToContents(8, 42);
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 24);
        ws.SheetView.FreezeRows(3);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo()
        => $"ventas_rubros_{DateTime.Today:yyyyMMdd}.xlsx";

    private static List<string> BuildHeaders(VentasRubrosPageDto page, bool comparativo)
    {
        var headers = new List<string> { "Rubro" };
        if (comparativo)
        {
            foreach (var unidad in page.UnidadesNegocio)
            {
                headers.Add(unidad.Descripcion);
                headers.Add($"{unidad.Descripcion} %");
            }

            headers.Add("Total");
            headers.Add("% total");
        }
        else
        {
            headers.Add("Total");
            headers.Add("% total");
            headers.Add("Artículos");
            headers.Add("Comprobantes");
        }

        return headers;
    }

    private static string BuildSubtitulo(VentasDashboardFilters filters, int totalRows, bool comparativo)
    {
        var parts = new List<string> { $"{totalRows} rubro(s)" };
        AddFilter(parts, "Desde", filters.FechaDesde?.ToString("dd/MM/yyyy"));
        AddFilter(parts, "Hasta", filters.FechaHasta?.ToString("dd/MM/yyyy"));
        AddFilter(parts, "Cliente", filters.Cliente);
        AddFilter(parts, "Usuario", filters.Usuario);
        AddFilter(parts, "Unidad de negocio", filters.UnidadesNegocio.Count == 0 ? "Todas" : string.Join(", ", filters.UnidadesNegocio));
        AddFilter(parts, "Depósito", filters.Deposito);
        AddFilter(parts, "TC", filters.TipoComprobante);
        AddFilter(parts, "Vista", comparativo ? "Comparativo por unidad de negocio" : "Consolidado");

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

    private static void SetHeaders(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
        }
    }

    private static void SetDecimalCell(IXLCell cell, decimal value, string format)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = format;
    }

    private static void SetPercentCell(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = "0.00%";
    }

    private static void SetRowBackground(IXLWorksheet ws, int row, int cols, string color = "#f8fafc")
    {
        for (var c = 1; c <= cols; c++)
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml(color);
    }

    private static string RubroTexto(string? rubro)
        => string.IsNullOrWhiteSpace(rubro) ? "Sin rubro" : rubro;
}
