using System.Text.Json;

namespace AlfaCore.Services;

/// <summary>
/// Datos del anuncio o publicación desde donde el cliente abrió el chat ("Click to WhatsApp").
/// Meta los manda en el objeto <c>referral</c> del primer mensaje entrante; el mensaje completo
/// ya queda guardado en CONV_MENSAJES.PayloadJson, así que se lee de ahí (también sirve para
/// mensajes viejos, sin cambiar la base).
/// </summary>
public sealed record WhatsAppAdReferral(string Titulo, string Texto, string Url, string Origen)
{
    /// <summary>"Anuncio" o "Publicación", según lo que informa Meta en source_type.</summary>
    public string OrigenNombre => string.Equals(Origen, "post", StringComparison.OrdinalIgnoreCase) ? "Publicación" : "Anuncio";

    public static bool TryParse(string? payloadJson, out WhatsAppAdReferral referral)
    {
        referral = default!;
        if (string.IsNullOrWhiteSpace(payloadJson) || !payloadJson.Contains("\"referral\"", StringComparison.Ordinal))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!TryFindReferral(doc.RootElement, out var element))
                return false;

            var titulo = ReadString(element, "headline");
            var texto = ReadString(element, "body");
            var url = SafeHttpUrl(ReadString(element, "source_url"));
            var origen = ReadString(element, "source_type");
            if (titulo.Length == 0 && texto.Length == 0 && url.Length == 0)
                return false;

            referral = new WhatsAppAdReferral(titulo, texto, url, origen);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // El payload guardado es el mensaje ("messages[i]"); por las dudas se acepta también el
    // envoltorio completo del webhook.
    private static bool TryFindReferral(JsonElement root, out JsonElement referral)
    {
        referral = default;
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        if (root.TryGetProperty("referral", out referral) && referral.ValueKind == JsonValueKind.Object)
            return true;
        if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in messages.EnumerateArray())
            {
                if (message.ValueKind == JsonValueKind.Object
                    && message.TryGetProperty("referral", out referral)
                    && referral.ValueKind == JsonValueKind.Object)
                    return true;
            }
        }
        return false;
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;

    private static string SafeHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : string.Empty;
}
