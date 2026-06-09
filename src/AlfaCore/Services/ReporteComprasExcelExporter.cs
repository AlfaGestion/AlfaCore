using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class ReporteComprasExcelExporter
{
    private static readonly System.Globalization.CultureInfo Cultura =
        System.Globalization.CultureInfo.GetCultureInfo("es-AR");

    // ── Resumen ───────────────────────────────────────────────────────────────

    public byte[] ExportarResumen(ResumenComprasDto resumen, FiltrosReporteCompras filtros)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Resumen de Compras");

        // Fila 1: título
        SetTitulo(ws, "Resumen de Compras", 7);

        // Fila 2: período y fecha de exportación
        SetSubtitulo(ws, BuildPeriodo(filtros), 7);

        // Fila 3: KPIs
        ws.Cell(3, 1).Value = "Total comprado";
        ws.Cell(3, 2).Value = resumen.TotalComprado;
        ws.Cell(3, 3).Value = "Neto";
        ws.Cell(3, 4).Value = resumen.NetoTotal;
        ws.Cell(3, 5).Value = "IVA";
        ws.Cell(3, 6).Value = resumen.IvaTotal;
        ws.Cell(3, 7).Value = "Comprobantes";
        ws.Cell(3, 8).Value = resumen.CantidadComprobantes;
        ws.Cell(3, 9).Value = "Proveedores";
        ws.Cell(3, 10).Value = resumen.CantidadProveedores;
        for (int c = 1; c <= 10; c++)
        {
            ws.Cell(3, c).Style.Font.Bold = true;
            ws.Cell(3, c).Style.Font.FontColor = XLColor.FromHtml("#1e3a5f");
        }

        // Fila 5: encabezados de tabla
        var headers = new[] { "Cuenta", "Proveedor", "Comprobantes", "Neto", "IVA", "Total", "% Part." };
        SetHeaders(ws, 5, headers);

        // Datos
        for (int i = 0; i < resumen.Filas.Count; i++)
        {
            var fila = resumen.Filas[i];
            int row = i + 6;
            ws.Cell(row, 1).Value = fila.Cuenta;
            ws.Cell(row, 2).Value = fila.RazonSocial;
            SetDecimalCell(ws.Cell(row, 3), fila.CantidadComprobantes);
            SetDecimalCell(ws.Cell(row, 4), fila.Neto);
            SetDecimalCell(ws.Cell(row, 5), fila.Iva);
            SetDecimalCell(ws.Cell(row, 6), fila.Total);
            ws.Cell(row, 7).Value = fila.Participacion.ToString("P1", Cultura);
            if (i % 2 == 1) SetRowBackground(ws, row, 7);
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(5);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Detalle ───────────────────────────────────────────────────────────────

    public byte[] ExportarDetalle(IReadOnlyList<DetalleComprasFilaDto> items, FiltrosReporteCompras filtros, int total)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Detalle de Compras");

        SetTitulo(ws, "Detalle de Compras", 10);
        SetSubtitulo(ws, $"{BuildPeriodo(filtros)} — {total} comprobantes", 10);

        var headers = new[] { "Fecha", "TC", "Comprobante", "Cuenta", "Proveedor", "Motivo", "Neto", "IVA", "Total", "Usuario" };
        SetHeaders(ws, 3, headers);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int row = i + 4;
            SetDateCell(ws.Cell(row, 1), item.Fecha);
            ws.Cell(row, 2).Value = item.Tc;
            ws.Cell(row, 3).Value = item.IdComprobante;
            ws.Cell(row, 4).Value = item.Cuenta;
            ws.Cell(row, 5).Value = item.RazonSocial;
            ws.Cell(row, 6).Value = item.DescMotivo;
            SetDecimalCell(ws.Cell(row, 7), item.Neto);
            SetDecimalCell(ws.Cell(row, 8), item.Iva);
            SetDecimalCell(ws.Cell(row, 9), item.Total);
            ws.Cell(row, 10).Value = item.Usuario;
            if (i % 2 == 1) SetRowBackground(ws, row, 10);
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(3);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Nombres de archivo ────────────────────────────────────────────────────

    public static string NombreArchivoResumen() =>
        $"reporte_compras_resumen_{DateTime.Today:yyyyMMdd}.xlsx";

    public static string NombreArchivoDetalle() =>
        $"reporte_compras_detalle_{DateTime.Today:yyyyMMdd}.xlsx";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SetTitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(1, 1);
        cell.Value = texto;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 12;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        cell.Style.Font.FontColor = XLColor.White;
        if (colspan > 1) ws.Range(1, 1, 1, colspan).Merge();
    }

    private static void SetSubtitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(2, 1);
        cell.Value = $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} — {texto}";
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Font.FontColor = XLColor.FromHtml("#64748b");
        if (colspan > 1) ws.Range(2, 1, 2, colspan).Merge();
    }

    private static void SetHeaders(IXLWorksheet ws, int row, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0369a1");
        }
    }

    private static void SetDecimalCell(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = "#,##0.00";
    }

    private static void SetDateCell(IXLCell cell, DateTime value)
    {
        cell.Value = value;
        cell.Style.DateFormat.Format = "dd/MM/yyyy";
    }

    private static void SetRowBackground(IXLWorksheet ws, int row, int cols)
    {
        for (int c = 1; c <= cols; c++)
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
    }

    private static string BuildPeriodo(FiltrosReporteCompras f)
    {
        var desde = f.FechaDesde?.ToString("dd/MM/yyyy") ?? "—";
        var hasta = f.FechaHasta?.ToString("dd/MM/yyyy") ?? "—";
        var extra = string.IsNullOrWhiteSpace(f.Proveedor) ? string.Empty : $" · Proveedor: {f.Proveedor}";
        var tc    = string.IsNullOrWhiteSpace(f.TipoComprobante) ? string.Empty : $" · TC: {f.TipoComprobante}";
        return $"Período: {desde} al {hasta}{extra}{tc}";
    }
}
