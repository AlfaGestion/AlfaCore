using System.Text.Json;

namespace AlfaCore.Services;

/// <summary>
/// Categoría normalizada de un mensaje de WhatsApp, independiente del "type" crudo de Meta -- la UI y
/// el recovery nunca deberían tener que conocer el nombre exacto que usa el webhook.
/// </summary>
public enum WhatsAppMessageKind
{
    Texto,
    Media,
    Ubicacion,
    Contacto,
    Reaccion,
    Sistema,
    Pedido,
    Desconocido
}

/// <summary>
/// Clasificación normalizada de un mensaje ya persistido (MessageType/PayloadJson/Origen ya guardados
/// en CONV_MENSAJES). No reemplaza el parseo del webhook -- interpreta lo ya guardado para que UI y
/// recovery no repitan cada uno su propio switch sobre MessageType/PayloadJson crudo. Punto único de
/// clasificación reusado por ordinary/history/echo (vía CONV_MENSAJES ya persistido) y por
/// Conversaciones.razor (render).
///
/// EsVisualizacionUnica: ningún payload real capturado hasta hoy (Cloud API de Meta) expone un
/// indicador confirmado de "visualización única" -- <see cref="WhatsAppMessageClassifier.Classify"/>
/// siempre devuelve false acá en vez de adivinar un nombre de campo. La arquitectura (este flag, el
/// texto de UI, la exclusión de recovery) ya está lista para activarse con una sola condición extra en
/// Classify apenas se confirme el campo real contra un payload de producción -- ver follow-up en el
/// commit. Los tests de esta capacidad construyen la clasificación directamente (no vía Classify) para
/// probar el comportamiento consumidor sin fabricar un schema de Meta no confirmado.
/// </summary>
public sealed record WhatsAppMessageClassification(
    WhatsAppMessageKind Kind,
    string TipoOriginal,
    bool EsMedia,
    bool EsSistema,
    bool EsOrigenBusiness,
    bool EsDesconocido,
    bool EsVisualizacionUnica,
    bool PuedeRecuperarseMedia);

public static class WhatsAppMessageClassifier
{
    private const string OrigenWhatsAppBusinessApp = "WHATSAPP_BUSINESS_APP";

    public static WhatsAppMessageClassification Classify(string? messageType, string? origen, string? payloadJson)
    {
        var normalized = ConversacionesService.NormalizeMessageType(messageType);
        var kind = normalized switch
        {
            "TEXT" => WhatsAppMessageKind.Texto,
            "IMAGE" or "AUDIO" or "VIDEO" or "DOCUMENT" or "STICKER" => WhatsAppMessageKind.Media,
            "LOCATION" => WhatsAppMessageKind.Ubicacion,
            "CONTACT" => WhatsAppMessageKind.Contacto,
            "REACTION" => WhatsAppMessageKind.Reaccion,
            "SYSTEM" => WhatsAppMessageKind.Sistema,
            "ORDER" => WhatsAppMessageKind.Pedido,
            _ => WhatsAppMessageKind.Desconocido
        };

        var esMedia = kind == WhatsAppMessageKind.Media;

        // Sin campo de view-once confirmado -- ver comentario de EsVisualizacionUnica en el record.
        const bool esVisualizacionUnica = false;

        return new WhatsAppMessageClassification(
            Kind: kind,
            TipoOriginal: ExtractRawMessageType(payloadJson),
            EsMedia: esMedia,
            EsSistema: kind == WhatsAppMessageKind.Sistema,
            EsOrigenBusiness: string.Equals((origen ?? string.Empty).Trim(), OrigenWhatsAppBusinessApp, StringComparison.OrdinalIgnoreCase),
            EsDesconocido: kind == WhatsAppMessageKind.Desconocido,
            EsVisualizacionUnica: esVisualizacionUnica,
            PuedeRecuperarseMedia: esMedia && !esVisualizacionUnica);
    }

    /// <summary>
    /// Lee message.type del PayloadJson persistido (envelope {metadata,message} de
    /// BuildIncomingWhatsAppMessagePayloadJson, o el objeto de mensaje directo) -- mismo parseo
    /// tolerante que ya usaba Conversaciones.razor.ExtractUnsupportedType, centralizado acá.
    /// </summary>
    public static string ExtractRawMessageType(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var message = doc.RootElement.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.Object
                ? messageProp
                : doc.RootElement;

            return message.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                ? typeProp.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
