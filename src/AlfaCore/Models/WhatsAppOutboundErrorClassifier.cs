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

    /// <summary>
    /// "Business eligibility payment issue" -- falta configurar moneda/método de pago de la WhatsApp
    /// Business Account en Meta Business Manager. A diferencia de AccountLocked, esta condición no es
    /// del mensaje puntual sino del WABA/número: AlfaNet es Technology Provider (el cliente le paga
    /// directamente a Meta) y nunca absorbe billing ni implementa Credit Sharing, así que la única
    /// acción posible es que el CLIENTE (no AlfaNet) complete su configuración de pago en Meta Business
    /// Manager. IsPersistentIntegrationCondition() marca este código para que el procesamiento del
    /// webhook actualice WhatsAppIntegrationHealth además de clasificar el mensaje individual -- debe
    /// seguir visible aunque el usuario cambie de conversación.
    /// </summary>
    public const string PaymentSetupRequired = "131042";

    // Los siguientes SON los códigos que Meta documenta públicamente para WhatsApp Cloud API
    // (developers.facebook.com/docs/whatsapp/cloud-api/support/error-codes) -- a diferencia de
    // AccountLocked, ninguno de éstos tiene todavía un caso confirmado con evidencia real de
    // producción en AlfaCore. Se mapean igual porque el pedido los pidió explícitamente por categoría,
    // pero si alguno resulta no coincidir con lo que realmente devuelve Meta en un caso real, hay que
    // corregirlo con esa evidencia, no asumir que esta lista es infalible.
    /// <summary>Mensaje de re-enganche fuera de la ventana de 24hs (texto libre sin plantilla).</summary>
    public const string ReEngagementWindowExpired = "131047";
    /// <summary>Código Graph estándar para token de acceso inválido o vencido.</summary>
    public const string InvalidAccessToken = "190";
    /// <summary>Mensaje no se pudo entregar (número inválido, no es WhatsApp, etc).</summary>
    public const string MessageUndeliverable = "131026";
    /// <summary>Límite de envíos por spam/calidad alcanzado.</summary>
    public const string SpamRateLimitHit = "131048";
    /// <summary>Límite de envíos de la aplicación/número alcanzado.</summary>
    public const string RateLimitHit = "130429";
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

    /// <summary>
    /// Códigos cuya causa es de la integración/WABA/número (no del mensaje puntual) y por lo tanto,
    /// además de clasificarse para mostrarse en el mensaje que falló, deben reflejarse en un estado
    /// persistente (WhatsAppIntegrationHealth) que sobrevive aunque el usuario cambie de conversación.
    /// Única fuente de verdad para esta decisión -- el procesamiento del webhook nunca debe volver a
    /// parsear errors[].code por su cuenta, siempre pasa por acá.
    /// </summary>
    public static bool IsPersistentIntegrationCondition(string? code)
        => string.Equals(code, WhatsAppGraphErrorCodes.PaymentSetupRequired, StringComparison.Ordinal);

    /// <summary>
    /// Extrae (code, message) directamente de un status callback de webhook (forma
    /// <c>{"errors":[{"code":...,"message":...}]}</c>), a diferencia de <see cref="ExtractGraphError"/>
    /// que espera la forma envuelta que persiste BuildDeliveryErrorPayload. Misma allowlist de códigos,
    /// una sola función de clasificación para ambos caminos (mensaje individual y salud persistente).
    /// </summary>
    public static string? ExtractWebhookErrorCode(string? rawStatusJson)
    {
        if (string.IsNullOrWhiteSpace(rawStatusJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(rawStatusJson);
            if (!doc.RootElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var error in errors.EnumerateArray())
                if (error.TryGetProperty("code", out var code))
                    return code.ToString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Clasifica directamente la excepción capturada por un envío (plantilla, reacción, texto) en el
    /// mismo momento del catch en la UI, sin esperar a que la fila del mensaje se recargue. Reconstruye
    /// la misma forma que BuildDeliveryErrorPayload (ConversacionesService) a partir de la excepción
    /// para reusar ExtractGraphError/ClassifyDeliveryError con una sola fuente de verdad de parseo.
    ///
    /// Antes de eso, chequea explícitamente si la causa es Vault/DataProtection inaccesible EN ESTE
    /// PROCESO (WhatsAppCredentialErrorClassifier) -- caso real: corriendo AlfaCore local contra una
    /// base cuyo SecureVault está protegido con un certificado que sólo existe en el servidor, el
    /// intento de resolver la credencial nunca llega a golpear Graph, pero antes esto caía igual en la
    /// clasificación genérica y en algunos caminos terminaba mostrando "WhatsApp requiere reconexión"
    /// -- semánticamente incorrecto: la credencial de Meta sigue siendo válida, sólo que este proceso
    /// no puede leerla. Chequear el TIPO de excepción primero evita esa confusión sin importar qué
    /// texto tenga el mensaje.
    /// </summary>
    public static AppUiMessage? ClassifyDeliverySendException(Exception ex, bool isProduction = false)
    {
        if (WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction) is { } credentialMessage)
            return credentialMessage;

        var baseException = ex.GetBaseException();
        var wrapped = JsonSerializer.Serialize(new
        {
            Error = baseException.Message,
            Type = baseException.GetType().FullName
        });
        return ClassifyDeliveryError("ERROR_ENVIO", wrapped);
    }

    /// <summary>Devuelve null cuando estadoEnvio no es un error de envío (nada que clasificar).</summary>
    public static AppUiMessage? ClassifyDeliveryError(string? estadoEnvio, string? deliveryPayloadJson)
    {
        if (!string.Equals(estadoEnvio?.Trim(), "ERROR_ENVIO", StringComparison.OrdinalIgnoreCase))
            return null;

        var (code, message) = ExtractGraphError(deliveryPayloadJson);
        return code switch
        {
            WhatsAppGraphErrorCodes.AccountLocked => AppUiMessage.Error(
                "Cuenta de WhatsApp bloqueada por Meta",
                "Meta bloqueó esta cuenta de WhatsApp Business. Mientras esté bloqueada no se pueden enviar ni recibir mensajes por este canal.",
                "Esto se resuelve directamente con Meta -- AlfaCore no puede desbloquearla ni reintentando el envío.",
                code),

            WhatsAppGraphErrorCodes.ReEngagementWindowExpired => AppUiMessage.Warning(
                "Ventana de atención vencida",
                "No podés enviar texto libre fuera de la ventana de atención de WhatsApp.",
                "Usá una plantilla aprobada para reabrir la conversación."),

            WhatsAppGraphErrorCodes.InvalidAccessToken => AppUiMessage.Error(
                "WhatsApp requiere reconexión",
                "La credencial de esta conexión de WhatsApp ya no es válida.",
                "Revisá la conexión con WhatsApp en Configuración."),

            WhatsAppGraphErrorCodes.MessageUndeliverable => AppUiMessage.Warning(
                "Destinatario inválido",
                "Meta no pudo entregarle el mensaje a este número.",
                "Verificá que el número tenga WhatsApp activo."),

            WhatsAppGraphErrorCodes.SpamRateLimitHit or WhatsAppGraphErrorCodes.RateLimitHit => AppUiMessage.Warning(
                "Envíos limitados temporalmente por Meta",
                "Meta limitó temporalmente los envíos de este número.",
                "Probá de nuevo más tarde."),

            // AlfaNet es Technology Provider: el cliente le paga directamente a Meta. Este mensaje
            // nunca debe sugerir que AlfaNet resuelve o absorbe el pago -- solo informa que el CLIENTE
            // tiene que completar su configuración de pago en Meta Business Manager.
            WhatsAppGraphErrorCodes.PaymentSetupRequired => AppUiMessage.ActionRequired(
                "WhatsApp requiere completar configuración de pagos",
                "Meta requiere que el cliente complete la configuración de pagos/facturación de esta cuenta de WhatsApp Business antes de poder enviar mensajes.",
                "No es necesario reconectar WhatsApp ni generar otro token -- esto se resuelve directamente en Meta Business Manager, por el titular de la cuenta.",
                code),

            _ => AppUiMessage.Error(
                "No se pudo enviar",
                string.IsNullOrWhiteSpace(message) ? "Meta no pudo entregar este mensaje." : message,
                string.Empty,
                code ?? string.Empty)
        };
    }
}
