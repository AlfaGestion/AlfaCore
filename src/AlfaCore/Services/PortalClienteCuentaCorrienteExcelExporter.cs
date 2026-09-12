using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

// Exportación Excel de la Cuenta corriente del Portal Cliente. Mismas columnas que la grilla en
// pantalla (Fecha, Comprobante, Descripción, Vencimiento, Importe, Saldo, Estado), respetando el
// rango de fechas, el filtro de saldo 0 y el orden de columna que el cliente tiene aplicados.
public sealed class PortalClienteCuentaCorrienteExcelExporter
{
    private static readonly string[] Headers = ["Fecha", "Comprobante", "Descripción", "Vencimiento", "Importe", "Saldo", "Estado"];

    public byte[] Exportar(
        IReadOnlyList<PortalClienteEstadoCuentaMovimientoDto> movimientos,
        string nombreEmpresa,
        string nombreCliente,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        string? sortBy,
        bool sortDescending)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Cuenta corriente");

        SetTitulo(ws, $"Cuenta corriente — {nombreEmpresa}");
        SetSubtitulo(ws, BuildSubtitulo(nombreCliente, movimientos.Count, fechaDesde, fechaHasta));

        var row = 4;
        SetHeaders(ws, row);
        row++;

        var ordenados = Ordenar(movimientos, sortBy, sortDescending);
        var i = 0;
        foreach (var mov in ordenados)
        {
            ws.Cell(row, 1).Value = mov.Fecha;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(row, 2).Value = mov.Comprobante;
            ws.Cell(row, 3).Value = mov.Detalle;

            if (mov.Vencimiento is { } vencimiento)
            {
                ws.Cell(row, 4).Value = vencimiento;
                ws.Cell(row, 4).Style.DateFormat.Format = "dd/MM/yyyy";
            }

            ws.Cell(row, 5).Value = mov.Importe;
            ws.Cell(row, 5).Style.NumberFormat.Format = "$ #,##0.00";
            ws.Cell(row, 6).Value = mov.Saldo;
            ws.Cell(row, 6).Style.NumberFormat.Format = "$ #,##0.00";
            ws.Cell(row, 7).Value = mov.Estado;

            if (i % 2 == 1)
                SetRowBackground(ws, row);

            row++;
            i++;
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 30);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo()
        => $"cuenta_corriente_{DateTime.Today:yyyyMMdd}.xlsx";

    private static IEnumerable<PortalClienteEstadoCuentaMovimientoDto> Ordenar(
        IReadOnlyList<PortalClienteEstadoCuentaMovimientoDto> movimientos, string? sortBy, bool sortDescending)
        => sortBy switch
        {
            "Comprobante" => sortDescending
                ? movimientos.OrderByDescending(m => m.Comprobante, StringComparer.OrdinalIgnoreCase)
                : movimientos.OrderBy(m => m.Comprobante, StringComparer.OrdinalIgnoreCase),
            "Detalle" => sortDescending
                ? movimientos.OrderByDescending(m => m.Detalle, StringComparer.OrdinalIgnoreCase)
                : movimientos.OrderBy(m => m.Detalle, StringComparer.OrdinalIgnoreCase),
            "Vencimiento" => sortDescending
                ? movimientos.OrderByDescending(m => m.Vencimiento)
                : movimientos.OrderBy(m => m.Vencimiento),
            "Importe" => sortDescending
                ? movimientos.OrderByDescending(m => m.Importe)
                : movimientos.OrderBy(m => m.Importe),
            "Estado" => sortDescending
                ? movimientos.OrderByDescending(m => m.Estado, StringComparer.OrdinalIgnoreCase)
                : movimientos.OrderBy(m => m.Estado, StringComparer.OrdinalIgnoreCase),
            _ => sortDescending
                ? movimientos.OrderByDescending(m => m.Fecha)
                : movimientos.OrderBy(m => m.Fecha)
        };

    private static string BuildSubtitulo(string nombreCliente, int totalRows, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var parts = new List<string> { $"{totalRows} movimiento(s)" };

        if (!string.IsNullOrWhiteSpace(nombreCliente))
            parts.Add($"Cliente: {nombreCliente}");

        if (fechaDesde is not null || fechaHasta is not null)
            parts.Add($"Período: {fechaDesde?.ToString("dd/MM/yyyy") ?? "—"} a {fechaHasta?.ToString("dd/MM/yyyy") ?? "—"}");

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
