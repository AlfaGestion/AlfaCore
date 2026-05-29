using System.Globalization;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class AuditoriaExcelExporter
{
    public byte[] ExportarUsuarios(AuditoriaUsuarioFilterDto filter, AuditoriaUsuarioResultDto result)
    {
        var columnas = BuildColumnas(filter);
        var filas = result.Items.Select(item => BuildFila(item, filter)).ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("AuditoriaUsuarios");

        ws.Cell(1, 1).Value = "Auditoría de usuarios";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        ws.Range(1, 1, 1, columnas.Count).Merge();

        ws.Cell(2, 1).Value = $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} · {result.TotalRegistros} registro(s)";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");
        ws.Range(2, 1, 2, columnas.Count).Merge();

        ws.Cell(3, 1).Value = BuildFiltroTexto(filter);
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#334155");
        ws.Range(3, 1, 3, columnas.Count).Merge();

        for (var i = 0; i < columnas.Count; i++)
        {
            var cell = ws.Cell(5, i + 1);
            cell.Value = columnas[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0ea5e9");
            cell.Style.Font.FontColor = XLColor.White;
        }

        for (var rowIndex = 0; rowIndex < filas.Count; rowIndex++)
        {
            var fila = filas[rowIndex];
            for (var colIndex = 0; colIndex < fila.Count; colIndex++)
            {
                var cell = ws.Cell(rowIndex + 6, colIndex + 1);
                WriteValue(cell, fila[colIndex]);
                if (rowIndex % 2 == 1)
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
            }
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(5);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivoUsuarios(AuditoriaUsuarioFilterDto filter)
    {
        var control = string.IsNullOrWhiteSpace(filter.TipoControl) ? "todos" : filter.TipoControl.Trim().ToLowerInvariant();
        return $"auditoria_usuarios_{control}_{DateTime.Today:yyyyMMdd}.xlsx";
    }

    private static List<string> BuildColumnas(AuditoriaUsuarioFilterDto filter)
    {
        if (string.Equals(filter.TipoControl, "ACTIVIDAD_USUARIO", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Fecha y hora",
                "Usuario",
                "PC",
                "Origen",
                "Tipo movimiento",
                "TC",
                "Comprobante",
                "Cuenta / nombre",
                "Descripción",
                "Importe",
                "Tiene asiento",
                "Tiene stock",
                "Motivo",
                "Observaciones"
            ];
        }

        if (string.Equals(filter.TipoControl, "CPRA_DUPLICADO", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Riesgo",
                "Proveedor / cuenta",
                "TC",
                "Número normalizado",
                "Importe",
                "Coincidencias",
                "Sucursales detectadas",
                "Usuarios detectados",
                "Fecha mínima",
                "Fecha máxima",
                "Impacto contable",
                "Comprobantes con asiento",
                "Detalle grupo"
            ];
        }

        if (string.Equals(filter.TipoControl, "IN_NO_GRABADO", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Fecha inicio",
                "Tipo control",
                "Riesgo",
                "Usuario",
                "Equipo",
                "TC",
                "N° comprobante",
                "Hora cancelación",
                "Minutos hasta cancelación",
                "Importe al cancelar",
                "Traza original del sistema",
                "Motivo",
                "Observaciones"
            ];
        }

        return
        [
            "Fecha y hora",
            "Tipo control",
            "Riesgo",
            "Usuario",
            "Equipo",
            "TC",
            "N° comprobante",
            "Cliente / cuenta",
            "Estado remito",
            "Importe base",
            "Diferencia",
            "Ocurrencias",
            "Motivo",
            "Observaciones"
        ];
    }

    private static List<object?> BuildFila(AuditoriaUsuarioRowDto item, AuditoriaUsuarioFilterDto filter)
    {
        if (string.Equals(filter.TipoControl, "ACTIVIDAD_USUARIO", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                item.FechaHora,
                item.Usuario,
                item.Pc,
                item.Origen,
                item.TipoMovimiento,
                item.TipoComprobante,
                item.IdComprobante,
                FormatCliente(item),
                item.Descripcion,
                item.ImporteOriginal,
                item.TieneAsiento ? "Sí" : "No",
                item.TieneStock ? "Sí" : "No",
                item.Motivo,
                item.Observaciones
            ];
        }

        if (string.Equals(filter.TipoControl, "CPRA_DUPLICADO", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                item.Riesgo,
                FormatCliente(item),
                item.TipoComprobante,
                item.NumeroNormalizado,
                item.ImporteOriginal,
                item.CantidadOcurrencias,
                item.SucursalesDetectadas,
                item.UsuariosDetectados,
                item.FechaHora,
                item.FechaHoraHasta,
                item.ImpactoContable,
                item.ContablesDuplicados,
                item.DetalleGrupo
            ];
        }

        if (string.Equals(filter.TipoControl, "IN_NO_GRABADO", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                item.FechaHora,
                item.TipoControlLabel,
                item.Riesgo,
                item.Usuario,
                item.Pc,
                item.TipoComprobante,
                item.IdComprobante,
                GetCancellationTime(item),
                GetCancellationMinutes(item),
                GetCancellationAmount(item),
                item.SystemUserDetalle,
                item.Motivo,
                item.Observaciones
            ];
        }

        return
        [
            item.FechaHora,
            item.TipoControlLabel,
            item.Riesgo,
            item.Usuario,
            item.Pc,
            item.TipoComprobante,
            item.IdComprobante,
            FormatCliente(item),
            string.IsNullOrWhiteSpace(item.EstadoFacturacion) ? "—" : item.EstadoFacturacion,
            item.ImporteOriginal,
            item.Diferencia,
            item.CantidadOcurrencias,
            item.Motivo,
            item.Observaciones
        ];
    }

    private static void WriteValue(IXLCell cell, object? value)
    {
        if (value is null)
        {
            cell.Value = string.Empty;
            return;
        }

        if (value is DateTime dt && dt != DateTime.MinValue)
        {
            cell.Value = dt;
            cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            return;
        }

        if (value is decimal dec)
        {
            cell.Value = dec;
            cell.Style.NumberFormat.Format = "#,##0.00";
            return;
        }

        cell.Value = value.ToString();
    }

    private static string BuildFiltroTexto(AuditoriaUsuarioFilterDto filter)
    {
        var partes = new List<string>();
        if (filter.Desde.HasValue || filter.Hasta.HasValue)
            partes.Add($"Período: {filter.Desde:dd/MM/yyyy} - {filter.Hasta:dd/MM/yyyy}");
        if (!string.IsNullOrWhiteSpace(filter.TipoControl))
            partes.Add($"Control: {filter.TipoControl}");
        if (!string.IsNullOrWhiteSpace(filter.Usuario))
            partes.Add($"Usuario: {filter.Usuario}");
        if (!string.IsNullOrWhiteSpace(filter.Pc))
            partes.Add($"PC: {filter.Pc}");
        if (!string.IsNullOrWhiteSpace(filter.TipoMovimiento))
            partes.Add($"Movimiento: {filter.TipoMovimiento}");
        if (!string.IsNullOrWhiteSpace(filter.TipoComprobante))
            partes.Add($"TC: {filter.TipoComprobante}");
        if (!string.IsNullOrWhiteSpace(filter.Riesgo))
            partes.Add($"Riesgo: {filter.Riesgo}");
        if (!string.IsNullOrWhiteSpace(filter.CuentaCliente))
            partes.Add($"Cuenta: {filter.CuentaCliente}");

        return partes.Count == 0 ? "Sin filtros adicionales" : string.Join(" · ", partes);
    }

    private static string FormatCliente(AuditoriaUsuarioRowDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Cliente) && !string.IsNullOrWhiteSpace(item.Cuenta))
            return $"{item.Cuenta} · {item.Cliente}";
        if (!string.IsNullOrWhiteSpace(item.Cliente))
            return item.Cliente;
        if (!string.IsNullOrWhiteSpace(item.Cuenta))
            return item.Cuenta;
        return "—";
    }

    private static string GetCancellationTime(AuditoriaUsuarioRowDto item)
        => TryParseCancellationTrace(item.SystemUserDetalle, out var when, out _) ? when : "—";

    private static string GetCancellationAmount(AuditoriaUsuarioRowDto item)
        => TryParseCancellationTrace(item.SystemUserDetalle, out _, out var amount) ? amount : "—";

    private static string GetCancellationMinutes(AuditoriaUsuarioRowDto item)
    {
        if (!TryParseCancellationTraceParts(item.SystemUserDetalle, out var cancellationAt, out _))
            return "—";

        if (!cancellationAt.HasValue || item.FechaHora == DateTime.MinValue || cancellationAt.Value < item.FechaHora)
            return "—";

        return Math.Round((cancellationAt.Value - item.FechaHora).TotalMinutes, 0).ToString("N0", CultureInfo.InvariantCulture);
    }

    private static bool TryParseCancellationTrace(string source, out string when, out string amount)
    {
        when = string.Empty;
        amount = string.Empty;
        if (!TryParseCancellationTraceParts(source, out var cancellationAt, out var cancellationAmount))
            return false;

        when = cancellationAt?.ToString("dd/MM/yyyy HH:mm:ss") ?? string.Empty;
        amount = cancellationAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(when) || !string.IsNullOrWhiteSpace(amount);
    }

    private static bool TryParseCancellationTraceParts(string source, out DateTime? cancellationAt, out decimal? cancellationAmount)
    {
        cancellationAt = null;
        cancellationAmount = null;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var dateMatch = Regex.Match(source, @"(?<fecha>\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}:\d{2})");
        if (dateMatch.Success && DateTime.TryParseExact(dateMatch.Groups["fecha"].Value, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            cancellationAt = parsedDate;

        var amountMatch = Regex.Match(source, @"\$\s*(?<importe>[0-9][0-9\.,]*)");
        if (amountMatch.Success && TryNormalizeAmount(amountMatch.Groups["importe"].Value, out var parsedAmount))
            cancellationAmount = parsedAmount;

        return cancellationAt.HasValue || cancellationAmount.HasValue;
    }

    private static bool TryNormalizeAmount(string source, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var normalized = source.Trim();
        var lastComma = normalized.LastIndexOf(',');
        var lastDot = normalized.LastIndexOf('.');
        if (lastComma > lastDot)
        {
            normalized = normalized.Replace(".", string.Empty);
            normalized = normalized.Replace(',', '.');
        }
        else
        {
            normalized = normalized.Replace(",", string.Empty);
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
