using System.Text.Json;

namespace AlfaCore.Models;

public static class WhatsAppGraphErrorCodes
{
    /// <summary>
    /// Meta bloqueó la cuenta de WhatsApp Business ("Business Account locked"). Confirmado en
    /// producción (Base84, 2026-09): el Graph GET de inspección puede devolver 200 con
    /// quality_rating=GREEN, platform_type=CLOUD_API e is_on_biz_app=true simultáneamente mientras la
    /// cuenta está bloqueada -- ninguno de esos campos es sinónimo de una cuenta operativa.
    /// </summary>
    public const string AccountLocked = "131031";
}

/// <summary>
/// Clasifica errores de envío de WhatsApp (EstadoEnvio=ERROR_ENVIO en CONV_MENSAJES) a partir del
/// PayloadJson que persiste BuildDeliveryErrorPayload (ConversacionesService), para mostrar una causa
/// accionable en vez del genérico "Error al enviar". Deliberadamente derivado en cada lectura y nunca
/// persistido como un nuevo flag de estado: un 131031 puntual en un mensaje viejo no debe convertirse
/// en un "bloqueado" permanente si la cuenta ya se desbloqueó -- por eso esto es puro cálculo sobre el
/// último payload conocido, no un campo nuevo en la base.
///
/// Sólo mapea causas confirmadas con evidencia real de producción (por ahora, 131031). Para el resto
/// devuelve el mensaje ya sanitizado por BuildDeliveryErrorPayload en vez de inventar clasificaciones
/// sin evidencia.
/// </summary>
public static class WhatsAppOutboundErrorClassifier
{
    /// <summary>
    /// Misma forma de extracción que WhatsAppRuntimeInspectionCommand.ExtractOutboundErrorInfo
    /// (Configuration/, comando de diagnóstico CLI). Se duplica a propósito acá en vez de compartir
    /// código entre capas: esta debe quedar alcanzable desde Models sin que la UI (Razor) dependa de
    /// Configuration/, que es una capa de arranque/CLI, no de servicio de aplicación.
    /// </summary>
    public static (string? Code, string? Message) ExtractGraphError(string? deliveryPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(deliveryPayloadJson))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(deliveryPayloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Error", out var errorProp)
                || errorProp.ValueKind != JsonValueKind.String)
                return (null, null);

            var errorText = errorProp.GetString() ?? string.Empty;
            var braceIndex = errorText.IndexOf('{');
            if (braceIndex < 0)
                return (null, null);

            using var inner = JsonDocument.Parse(errorText[braceIndex..]);
            if (inner.RootElement.ValueKind != JsonValueKind.Object
                || !inner.RootElement.TryGetProperty("error", out var innerError)
                || innerError.ValueKind != JsonValueKind.Object)
                return (null, null);

            var code = innerError.TryGetProperty("code", out var codeProp) ? codeProp.ToString() : null;
            var message = innerError.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String
                ? messageProp.GetString()
                : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public static bool IsAccountLocked(string? deliveryPayloadJson)
        => string.Equals(ExtractGraphError(deliveryPayloadJson).Code, WhatsAppGraphErrorCodes.AccountLocked, StringComparison.Ordinal);

    /// <summary>Devuelve null cuando estadoEnvio no es un error de envío (nada que clasificar).</summary>
    public static AppUiMessage? ClassifyDeliveryError(string? estadoEnvio, string? deliveryPayloadJson)
    {
        if (!string.Equals(estadoEnvio?.Trim(), "ERROR_ENVIO", StringComparison.OrdinalIgnoreCase))
            return null;

        var (code, message) = ExtractGraphError(deliveryPayloadJson);
        if (string.Equals(code, WhatsAppGraphErrorCodes.AccountLocked, StringComparison.Ordinal))
            return AppUiMessage.Error(
                "Cuenta de WhatsApp bloqueada por Meta",
                "Meta bloqueó esta cuenta de WhatsApp Business. Mientras esté bloqueada no se pueden enviar ni recibir mensajes por este canal.",
                "Esto se resuelve directamente con Meta -- AlfaCore no puede desbloquearla ni reintentando el envío.",
                code ?? string.Empty);

        return AppUiMessage.Error(
            "No se pudo enviar",
            string.IsNullOrWhiteSpace(message) ? "Meta no pudo entregar este mensaje." : message,
            string.Empty,
            code ?? string.Empty);
    }
}
