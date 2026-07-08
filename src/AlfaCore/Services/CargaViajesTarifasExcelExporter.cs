using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class CargaViajesTarifasExcelExporter
{
    public byte[] Exportar(
        IReadOnlyList<CargaViajeTarifaGridItemDto> rows,
        CargaViajesFilters filters,
        string agruparPor)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Tarifas");

        SetTitulo(ws, "Tarifas de Carga de Viajes", 10);
        SetSubtitulo(ws, BuildSubtitulo(filters, rows.Count, agruparPor), 10);
        SetHeaders(ws, 3, ["Nombre", "Tipo tarifa", "Cliente / Chofer", "Destino", "Tipo vehículo", "Importe", "Activo", "Última modificación", "Usuario modif.", "Id lista"]);

        var rowIndex = 4;
        foreach (var group in BuildGroups(rows, agruparPor))
        {
            if (group.ShowHeader)
            {
                ws.Cell(rowIndex, 1).Value = group.Label;
                ws.Cell(rowIndex, 2).Value = $"{group.Items.Count} tarifa(s)";
                ws.Cell(rowIndex, 6).Value = group.TotalImporte;
                ws.Range(rowIndex, 1, rowIndex, 10).Style.Font.Bold = true;
                ws.Range(rowIndex, 1, rowIndex, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#e0f2fe");
                ws.Range(rowIndex, 1, rowIndex, 10).Style.Font.FontColor = XLColor.FromHtml("#0f172a");
                ws.Cell(rowIndex, 6).Style.NumberFormat.Format = "$ #,##0.00";
                rowIndex++;
            }

            for (var i = 0; i < group.Items.Count; i++, rowIndex++)
            {
                var item = group.Items[i];
                ws.Cell(rowIndex, 1).Value = item.Nombre;
                ws.Cell(rowIndex, 2).Value = item.TarifaFletero ? "Fletero" : "Cliente";
                ws.Cell(rowIndex, 3).Value = GetEntidadDisplay(item);
                ws.Cell(rowIndex, 4).Value = ExtractVisibleText(item.Destino);
                ws.Cell(rowIndex, 5).Value = ExtractVisibleText(item.TipoVehiculo);
                ws.Cell(rowIndex, 6).Value = item.Importe;
                ws.Cell(rowIndex, 7).Value = item.Activo ? "Activo" : "Desactivado";
                ws.Cell(rowIndex, 8).Value = FormatUltimaModificacion(item);
                ws.Cell(rowIndex, 9).Value = item.UsuarioModificacion;
                ws.Cell(rowIndex, 10).Value = item.IdLista;
                ws.Cell(rowIndex, 6).Style.NumberFormat.Format = "$ #,##0.00";

                if (i % 2 == 1)
                    SetRowBackground(ws, rowIndex, 10);
            }
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 24);
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 24);
        ws.Column(4).Width = Math.Max(ws.Column(4).Width, 24);
        ws.Column(5).Width = Math.Max(ws.Column(5).Width, 24);
        ws.Column(8).Width = Math.Max(ws.Column(8).Width, 22);
        ws.SheetView.FreezeRows(3);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo() =>
        $"carga_viajes_tarifas_{DateTime.Today:yyyyMMdd}.xlsx";

    private static string BuildSubtitulo(CargaViajesFilters filters, int totalRows, string agruparPor)
    {
        var parts = new List<string> { $"{totalRows} tarifa(s)" };

        AddFilter(parts, "Texto", filters.Texto);
        AddFilter(parts, "Cliente", filters.Cliente);
        AddFilter(parts, "Chofer", filters.Chofer);
        AddFilter(parts, "Destino", filters.Destino);
        AddFilter(parts, "Tipo vehículo", filters.TipoVehiculo);

        if (filters.TarifaFletero is "0" or "1")
            parts.Add(filters.TarifaFletero == "1" ? "Tipo: Fleteros" : "Tipo: Clientes");

        if (filters.Activo is "0" or "1")
            parts.Add(filters.Activo == "1" ? "Activo: Sí" : "Activo: No");

        var agrupacion = GetAgruparPorLabel(agruparPor);
        if (!string.IsNullOrWhiteSpace(agrupacion))
            parts.Add($"Agrupar por: {agrupacion}");

        return $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} · {string.Join(" · ", parts)}";
    }

    private static List<TarifaExcelGroup> BuildGroups(IReadOnlyList<CargaViajeTarifaGridItemDto> rows, string agruparPor)
    {
        var normalized = NormalizeAgruparPor(agruparPor);
        if (normalized == CargaViajesViewGroupKeys.None)
        {
            return
            [
                new TarifaExcelGroup
                {
                    ShowHeader = false,
                    Label = string.Empty,
                    TotalImporte = rows.Sum(x => x.Importe),
                    Items = rows.ToList()
                }
            ];
        }

        return rows
            .Select((row, index) => new { row, index })
            .GroupBy(x => new
            {
                Tipo = GetGroupType(normalized),
                Nombre = GetGroupValue(x.row, normalized)
            })
            .Select(g => new TarifaExcelGroup
            {
                ShowHeader = true,
                Label = $"{g.Key.Tipo}: {g.Key.Nombre}",
                TotalImporte = g.Sum(x => x.row.Importe),
                FirstIndex = g.Min(x => x.index),
                Items = g.Select(x => x.row).ToList()
            })
            .OrderBy(g => g.FirstIndex)
            .ToList();
    }

    private static string NormalizeAgruparPor(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            CargaViajesViewGroupKeys.Cliente => CargaViajesViewGroupKeys.Cliente,
            CargaViajesViewGroupKeys.Chofer => CargaViajesViewGroupKeys.Chofer,
            CargaViajesViewGroupKeys.Fletero => CargaViajesViewGroupKeys.Chofer,
            _ => CargaViajesViewGroupKeys.None
        };

    private static string GetAgruparPorLabel(string? value)
        => NormalizeAgruparPor(value) switch
        {
            CargaViajesViewGroupKeys.Cliente => "Cliente",
            CargaViajesViewGroupKeys.Chofer => "Chofer / fletero",
            _ => string.Empty
        };

    private static string GetGroupType(string agruparPor)
        => agruparPor switch
        {
            CargaViajesViewGroupKeys.Cliente => "Cliente",
            CargaViajesViewGroupKeys.Chofer => "Chofer / fletero",
            _ => string.Empty
        };

    private static string GetGroupValue(CargaViajeTarifaGridItemDto row, string agruparPor)
        => agruparPor switch
        {
            CargaViajesViewGroupKeys.Cliente => row.TarifaFletero ? "Sin cliente" : GetEntidadDisplay(row),
            CargaViajesViewGroupKeys.Chofer => row.TarifaFletero ? GetEntidadDisplay(row) : "Sin chofer / fletero",
            _ => string.Empty
        };

    private static string GetEntidadDisplay(CargaViajeTarifaGridItemDto row)
        => ExtractVisibleText(row.TarifaFletero ? row.Chofer : row.Cliente);

    private static string ExtractVisibleText(string? display)
    {
        var value = (display ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var separator = value.IndexOf(" - ", StringComparison.Ordinal);
        if (separator < 0)
            return value;

        var visible = value[(separator + 3)..].Trim();
        return string.IsNullOrWhiteSpace(visible) ? value : visible;
    }

    private static string FormatUltimaModificacion(CargaViajeTarifaGridItemDto row)
    {
        if (!row.FechaHoraModificacion.HasValue)
            return string.IsNullOrWhiteSpace(row.UsuarioModificacion)
                ? string.Empty
                : $"por {row.UsuarioModificacion.Trim()}";

        var when = row.FechaHoraModificacion.Value.ToString("dd/MM/yyyy HH:mm");
        return string.IsNullOrWhiteSpace(row.UsuarioModificacion)
            ? when
            : $"{when} · {row.UsuarioModificacion.Trim()}";
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

    private sealed class TarifaExcelGroup
    {
        public bool ShowHeader { get; init; }
        public string Label { get; init; } = string.Empty;
        public decimal TotalImporte { get; init; }
        public int FirstIndex { get; init; }
        public List<CargaViajeTarifaGridItemDto> Items { get; init; } = [];
    }
}
