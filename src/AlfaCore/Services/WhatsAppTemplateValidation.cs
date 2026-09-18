using AlfaCore.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public static class WhatsAppTemplateValidation
{
    public static void EnsureScope(ConversacionPlantillaDto template, string? wabaId)
    {
        if (!string.Equals(template.WabaId ?? string.Empty, wabaId ?? string.Empty, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("La plantilla no pertenece al WhatsApp seleccionado.");
    }

    public static void ValidateSend(ConversacionPlantillaDto template, IReadOnlyList<string> values)
    {
        if (!template.Activa)
            throw new InvalidOperationException("La plantilla está archivada. Elegí una plantilla activa.");
        var unsupported = UnsupportedReason(template);
        if (unsupported.Length > 0) throw new InvalidOperationException(unsupported);
        var indexes = Regex.Matches(template.CuerpoTexto, @"\{\{\s*(\d+)\s*\}\}")
            .Select(m => int.TryParse(m.Groups[1].Value, out var index) ? index : -1).Distinct().Order().ToArray();
        if (!indexes.SequenceEqual(Enumerable.Range(1, indexes.Length)))
            throw new InvalidOperationException("Las variables BODY deben ser consecutivas desde {{1}}.");
        if (values.Count != indexes.Length || values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"La plantilla requiere exactamente {indexes.Length} valores BODY no vacíos, en el orden indicado.");
    }

    public static string UnsupportedReason(ConversacionPlantillaDto template)
    {
        const string message = "Esta plantilla requiere componentes que AlfaCore todavía no permite completar (HEADER variable/media, botones o variables con nombre). Elegí una plantilla con BODY posicional y encabezado fijo.";
        if (template.EncabezadoTexto.Contains("{{", StringComparison.Ordinal)
            || Regex.IsMatch(template.CuerpoTexto, @"\{\{\s*[^\d\s}][^}]*\}\}")) return message;
        if (string.IsNullOrWhiteSpace(template.ComponentesMetaJson)) return string.Empty;
        using var document = JsonDocument.Parse(template.ComponentesMetaJson);
        foreach (var component in document.RootElement.EnumerateArray())
        {
            var type = component.GetProperty("type").GetString()?.ToUpperInvariant();
            if (type == "BODY" || type == "FOOTER") continue;
            if (type != "HEADER") return message;
            if (!component.TryGetProperty("format", out var format) || format.GetString() != "TEXT") return message;
            if (component.TryGetProperty("text", out var text) && (text.GetString() ?? "").Contains("{{", StringComparison.Ordinal)) return message;
        }
        return string.Empty;
    }

    public static string MetaErrorMessage(Exception exception)
    {
        if (exception is MetaWhatsAppManagementException management)
            return $"Meta rechazó la consulta (código {management.ErrorCode}). " +
                (management.RequiresReauthorization ? "Revisá los permisos y la credencial de la integración seleccionada." : "Reintentá la sincronización del WhatsApp seleccionado.");
        var start = exception.Message.IndexOf('{');
        if (start < 0) return "No se pudo completar la operación con Meta. Revisá la conexión y sincronizá la plantilla antes de reintentar.";
        try
        {
            using var document = JsonDocument.Parse(exception.Message[start..]);
            var error = document.RootElement.GetProperty("error");
            var code = error.TryGetProperty("code", out var c) ? c.ToString() : "sin código";
            var trace = error.TryGetProperty("fbtrace_id", out var t) ? $" Referencia Meta: {t.GetString()}." : "";
            var advice = code switch
            {
                "190" or "10" or "200" => "Revisá la credencial y los permisos de la integración seleccionada.",
                "132000" or "132012" => "Revisá la cantidad, el orden y el tipo de parámetros de la plantilla.",
                "132001" => "Sincronizá la plantilla y verificá su idioma y WABA.",
                "132015" or "132016" => "La plantilla está pausada o deshabilitada. Elegí otra plantilla aprobada.",
                _ => "Revisá el estado de la plantilla en Meta y reintentá la sincronización."
            };
            return $"Meta rechazó la operación (código {code}). {advice}{trace}";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "Meta devolvió una respuesta inválida. Sincronizá antes de reintentar; consultá el código de incidente con soporte.";
        }
    }
}
