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
    /// Texto para lo que WhatsApp no deja ver fuera del celular. Meta lo manda como type "unsupported"
    /// (o "errors" en el historial) con el error 131051 y sin decir qué era (raw_type "unknown" en todos
    /// los payloads reales revisados): típicamente fotos/videos de visualización única.
    /// </summary>
    public const string SoloEnCelularText = "Este mensaje solo se puede ver desde WhatsApp en el celular (por ejemplo, una foto o un video de visualización única).";

    public const string MensajeEliminadoText = "Se eliminó un mensaje.";

    /// <summary>Tipos crudos de Meta que se muestran con <see cref="SoloEnCelularText"/>.</summary>
    public static bool EsSoloEnCelular(string? rawType)
        => (rawType ?? string.Empty).Trim().ToLowerInvariant() is "unsupported" or "errors" or "media_placeholder";

    public static bool EsEdicion(string? rawType)
        => string.Equals((rawType ?? string.Empty).Trim(), "edit", StringComparison.OrdinalIgnoreCase);

    public static bool EsEliminacion(string? rawType)
        => string.Equals((rawType ?? string.Empty).Trim(), "revoke", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Texto de un mensaje editado (type "edit"). No hay un payload real confirmado todavía, así que se
    /// busca el texto nuevo en las formas posibles (edit.message.text.body, edit.text.body, edit.body...)
    /// sin fallar si no está.
    /// </summary>
    public static string BuildEditedText(JsonElement message)
    {
        var nuevo = FindEditedBody(message);
        return string.IsNullOrWhiteSpace(nuevo)
            ? "✏️ Editó un mensaje (el texto nuevo se ve en el celular)."
            : $"✏️ Mensaje editado: {nuevo.Trim()}";
    }

    public static string BuildEditedText(string? payloadJson)
    {
        var message = TryGetMessageElement(payloadJson, out var doc);
        using (doc)
            return message is JsonElement m ? BuildEditedText(m) : BuildEditedText(default(JsonElement));
    }

    /// <summary>Id (wamid) del mensaje original de una edición, si vino en alguna de sus formas.</summary>
    public static string FindEditOriginalId(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (message.TryGetProperty("edit", out var edit) && edit.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "original_message_id", "message_id", "id" })
            {
                if (edit.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prop.GetString()))
                    return prop.GetString()!;
            }
            if (edit.TryGetProperty("context", out var editContext) && editContext.ValueKind == JsonValueKind.Object
                && editContext.TryGetProperty("id", out var editContextId) && editContextId.ValueKind == JsonValueKind.String)
                return editContextId.GetString() ?? string.Empty;
        }

        return message.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Object
               && context.TryGetProperty("id", out var contextId) && contextId.ValueKind == JsonValueKind.String
            ? contextId.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Para tipos que AlfaCore no conoce (nuevos de Meta, mensajes enviados por empresas, etc.): busca un
    /// texto legible dentro del objeto del tipo (body/text/caption/title, hasta 3 niveles). Vacío si no hay.
    /// </summary>
    public static string FindReadableText(JsonElement message, string type)
    {
        if (message.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(type)
            || !message.TryGetProperty(type, out var content))
            return string.Empty;

        return FindTextRecursive(content, 0);
    }

    private static string FindEditedBody(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (message.TryGetProperty("edit", out var edit) && edit.ValueKind == JsonValueKind.Object)
        {
            var found = FindTextRecursive(edit, 0);
            if (!string.IsNullOrWhiteSpace(found))
                return found;
        }

        return message.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Object
               && text.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
            ? body.GetString() ?? string.Empty
            : string.Empty;
    }

    private static readonly string[] TextPropertyNames = ["body", "text", "caption", "title", "new_text"];

    private static string FindTextRecursive(JsonElement element, int depth)
    {
        if (depth > 3)
            return string.Empty;

        switch (element.ValueKind)
        {
            case JsonValueKind.String when depth > 0:
                return element.GetString() ?? string.Empty;
            case JsonValueKind.Object:
                foreach (var name in TextPropertyNames)
                {
                    if (!element.TryGetProperty(name, out var prop))
                        continue;
                    if (prop.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prop.GetString()))
                        return prop.GetString()!;
                    if (prop.ValueKind == JsonValueKind.Object)
                    {
                        var nested = FindTextRecursive(prop, depth + 1);
                        if (!string.IsNullOrWhiteSpace(nested))
                            return nested;
                    }
                }
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object)
                        continue;
                    var nested = FindTextRecursive(prop.Value, depth + 1);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
                return string.Empty;
            default:
                return string.Empty;
        }
    }

    private static JsonElement? TryGetMessageElement(string? payloadJson, out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        try
        {
            doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.Object
                ? messageProp
                : doc.RootElement;
        }
        catch (JsonException)
        {
            return null;
        }
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
