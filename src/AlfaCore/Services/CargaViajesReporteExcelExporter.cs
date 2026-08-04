using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class CargaViajesReporteExcelExporter
{
    public byte[] ExportarChoferesFleteros(IReadOnlyList<CargaViajeReporteLiquidacionRowDto> rows, CargaViajesReporteLiquidacionFilters filters)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Reporte");

        var conceptColumns = GetFleteroConceptColumns(rows);
        var headers = new List<string> { "Fecha", "Cliente", "Chofer / Fletero", "Destino", "Tipo vehículo", "Importe chofer/fletero" };
        headers.AddRange(conceptColumns);
        headers.Add("Estado");
        headers.Add("Estado pago");

        SetTitulo(ws, "Reporte de Viajes · Choferes y Fleteros", headers.Count);
        SetSubtitulo(ws, BuildSubtitulo(filters, rows.Count), headers.Count);

        ws.Cell(3, 1).Value = "Viajes";
        ws.Cell(3, 2).Value = rows.Count;
        ws.Cell(3, 3).Value = "Total chofer/fletero";
        ws.Cell(3, 4).Value = rows.Sum(x => x.TotalFlete);
        ws.Cell(3, 4).Style.NumberFormat.Format = "$ #,##0.00";
        ws.Range(3, 1, 3, 4).Style.Font.Bold = true;

        SetHeaders(ws, 5, headers.ToArray());

        var rowIndex = 6;
        for (var i = 0; i < rows.Count; i++, rowIndex++)
        {
            var item = rows[i];
            var col = 1;
            ws.Cell(rowIndex, col++).Value = item.Fecha;
            ws.Cell(rowIndex, col - 1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(rowIndex, col++).Value = item.ClienteNombre;
            ws.Cell(rowIndex, col++).Value = item.ChoferNombre;
            ws.Cell(rowIndex, col++).Value = item.DestinoDescripcion;
            ws.Cell(rowIndex, col++).Value = item.TipoVehiculoDescripcion;
            ws.Cell(rowIndex, col).Value = item.TotalFlete;
            ws.Cell(rowIndex, col).Style.NumberFormat.Format = "$ #,##0.00";
            col++;

            foreach (var columna in conceptColumns)
            {
                var concepto = item.ConceptosFletero.FirstOrDefault(c => c.Descripcion == columna);
                if (concepto is not null)
                {
                    ws.Cell(rowIndex, col).Value = concepto.Importe;
                    ws.Cell(rowIndex, col).Style.NumberFormat.Format = "$ #,##0.00";
                }
                col++;
            }

            ws.Cell(rowIndex, col++).Value = item.Estado;
            ws.Cell(rowIndex, col).Value = item.FletePagado ? "Pagado" : "Pendiente";

            if (i % 2 == 1)
                SetRowBackground(ws, rowIndex, headers.Count);
        }

        FinalizeSheet(ws, headers.Count, 5);
        return SaveWorkbook(wb);
    }

    public byte[] ExportarClientes(IReadOnlyList<CargaViajeReporteClienteRowDto> rows, CargaViajesReporteLiquidacionFilters filters)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Reporte");

        var conceptColumns = GetClienteConceptColumns(rows);
        var headers = new List<string> { "Fecha", "Comprobante", "Cliente", "Destino", "Chofer / Fletero", "Tipo vehículo" };
        headers.AddRange(conceptColumns);
        headers.Add("Total cliente");
        headers.Add("Peaje");

        SetTitulo(ws, "Reporte de Viajes · Clientes", headers.Count);
        SetSubtitulo(ws, BuildSubtitulo(filters, rows.Count), headers.Count);

        ws.Cell(3, 1).Value = "Viajes";
        ws.Cell(3, 2).Value = rows.Count;
        ws.Cell(3, 3).Value = "Total cliente";
        ws.Cell(3, 4).Value = rows.Sum(x => x.Viaje.TotalCliente);
        ws.Cell(3, 4).Style.NumberFormat.Format = "$ #,##0.00";
        ws.Cell(3, 5).Value = "Total peajes";
        ws.Cell(3, 6).Value = rows.Sum(x => x.Viaje.Peaje);
        ws.Cell(3, 6).Style.NumberFormat.Format = "$ #,##0.00";
        ws.Range(3, 1, 3, 6).Style.Font.Bold = true;

        SetHeaders(ws, 5, headers.ToArray());

        var rowIndex = 6;
        for (var i = 0; i < rows.Count; i++, rowIndex++)
        {
            var item = rows[i];
            var col = 1;
            ws.Cell(rowIndex, col++).Value = item.Viaje.Fecha;
            ws.Cell(rowIndex, col - 1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(rowIndex, col++).Value = item.Viaje.IdComprobante;
            ws.Cell(rowIndex, col++).Value = item.Viaje.ClienteNombre;
            ws.Cell(rowIndex, col++).Value = item.Viaje.DestinoDescripcion;
            ws.Cell(rowIndex, col++).Value = item.Viaje.ChoferNombre;
            ws.Cell(rowIndex, col++).Value = item.Viaje.TipoVehiculoDescripcion;

            foreach (var columna in conceptColumns)
            {
                var concepto = item.Conceptos.FirstOrDefault(c => c.Descripcion == columna);
                if (concepto is not null)
                {
                    ws.Cell(rowIndex, col).Value = concepto.Importe;
                    ws.Cell(rowIndex, col).Style.NumberFormat.Format = "$ #,##0.00";
                }
                col++;
            }

            ws.Cell(rowIndex, col).Value = item.Viaje.TotalCliente;
            ws.Cell(rowIndex, col).Style.NumberFormat.Format = "$ #,##0.00";
            col++;
            if (item.Viaje.Peaje > 0m)
            {
                ws.Cell(rowIndex, col).Value = item.Viaje.Peaje;
                ws.Cell(rowIndex, col).Style.NumberFormat.Format = "$ #,##0.00";
            }

            if (i % 2 == 1)
                SetRowBackground(ws, rowIndex, headers.Count);
        }

        FinalizeSheet(ws, headers.Count, 5);
        return SaveWorkbook(wb);
    }

    public byte[] ExportarTodoJunto(IReadOnlyList<CargaViajeReporteClienteRowDto> rows, CargaViajesReporteLiquidacionFilters filters)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Reporte");

        SetTitulo(ws, "Reporte de Viajes · Todo junto", 2);
        SetSubtitulo(ws, BuildSubtitulo(filters, rows.Count), 2);

        var totalCliente = rows.Sum(x => x.Viaje.TotalCliente);
        var totalFletero = rows.Sum(x => x.Viaje.TotalFletero);

        var etiquetas = new (string Label, decimal Valor)[]
        {
            ("Cantidad total de viajes", rows.Count),
            ("Total cliente", totalCliente),
            ("Total chofer/fletero", totalFletero),
            ("Saldo", totalCliente - totalFletero)
        };

        var rowIndex = 4;
        foreach (var (label, valor) in etiquetas)
        {
            ws.Cell(rowIndex, 1).Value = label;
            ws.Cell(rowIndex, 1).Style.Font.Bold = true;
            ws.Cell(rowIndex, 2).Value = valor;
            if (label != "Cantidad total de viajes")
                ws.Cell(rowIndex, 2).Style.NumberFormat.Format = "$ #,##0.00";
            rowIndex++;
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 26);
        return SaveWorkbook(wb);
    }

    public static string NombreArchivo() =>
        $"reporte_viajes_{DateTime.Today:yyyyMMdd}.xlsx";

    private static IReadOnlyList<string> GetFleteroConceptColumns(IReadOnlyList<CargaViajeReporteLiquidacionRowDto> rows)
    {
        var columns = new List<string>();
        foreach (var row in rows)
        {
            foreach (var concepto in row.ConceptosFletero)
            {
                if (!columns.Contains(concepto.Descripcion))
                    columns.Add(concepto.Descripcion);
            }
        }

        return columns;
    }

    private static IReadOnlyList<string> GetClienteConceptColumns(IReadOnlyList<CargaViajeReporteClienteRowDto> rows)
    {
        var columns = new List<string>();
        foreach (var row in rows)
        {
            foreach (var concepto in row.Conceptos)
            {
                if (!columns.Contains(concepto.Descripcion))
                    columns.Add(concepto.Descripcion);
            }
        }

        return columns;
    }

    private static string BuildSubtitulo(CargaViajesReporteLiquidacionFilters filters, int totalRows)
    {
        var desde = filters.FechaDesde?.ToString("dd/MM/yyyy") ?? "—";
        var hasta = filters.FechaHasta?.ToString("dd/MM/yyyy") ?? "—";
        return $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} · Período {desde} al {hasta} · {totalRows} viaje(s)";
    }

    private static void FinalizeSheet(IXLWorksheet ws, int columnCount, int freezeRow)
    {
        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(freezeRow);
    }

    private static byte[] SaveWorkbook(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
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
