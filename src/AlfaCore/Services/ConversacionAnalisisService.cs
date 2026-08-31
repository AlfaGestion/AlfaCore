using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class ConversacionAnalisisService(IHttpClientFactory httpClientFactory) : IConversacionAnalisisService
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    public async Task<ConversacionAnalisisDto?> AnalizarAsync(IReadOnlyList<ConversacionMensajeDto> mensajes, CancellationToken ct = default)
    {
        const string system = """
            Analizás una conversación de atención al cliente y devolvés SOLO JSON válido
            con esta forma exacta: {"resumen":"...","intencion":"...","sentimiento":"..."}.
            - resumen: 1-2 frases, en español rioplatense, qué necesita/pasó.
            - intencion: etiqueta corta (ej. "Consulta de precio", "Reclamo", "Soporte técnico",
              "Pedido", "Seguimiento", "Otro").
            - sentimiento: exactamente uno de POSITIVO, NEUTRO o NEGATIVO (del cliente).
            No inventes datos que no estén en la conversación.
            """;
        var texto = await CallOpenAiAsync(system, BuildTranscript(mensajes), ct);
        return ParseAnalisis(texto);
    }

    public async Task<ConversacionExtraccionDto?> ExtraerOportunidadAsync(IReadOnlyList<ConversacionMensajeDto> mensajes, CancellationToken ct = default)
    {
        const string system = """
            De esta conversación de atención al cliente extraés datos para crear una oportunidad
            comercial. Devolvés SOLO JSON válido con la forma: {"titulo":"...","descripcion":"..."}.
            - titulo: título corto (máx ~60 caracteres) que resuma qué busca/necesita el cliente.
            - descripcion: 1-3 frases con el detalle de lo que pide (productos, servicio, cantidades,
              contexto). En español rioplatense.
            No inventes datos que no estén en la conversación; si no hay pedido claro, dejá campos vacíos.
            """;
        var texto = await CallOpenAiAsync(system, BuildTranscript(mensajes), ct);
        return ParseExtraccion(texto);
    }

    public async Task<string?> TransformarTextoAsync(string texto, string instruccion, CancellationToken ct = default)
    {
        var borrador = (texto ?? string.Empty).Trim();
        if (borrador.Length == 0)
            return null;

        var system = $"""
            Reescribí el texto del usuario según esta instrucción: {instruccion}.
            Mantené la intención y los datos; no agregues información, saludos ni explicaciones nuevas.
            Devolvé SOLO el texto final, sin comillas ni comentarios.
            """;
        var resultado = await CallOpenAiAsync(system, borrador, ct);
        return string.IsNullOrWhiteSpace(resultado) ? null : resultado.Trim();
    }

    private async Task<string?> CallOpenAiAsync(string systemPrompt, string transcript, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || transcript.Length == 0)
            return null;

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var payload = BuildChatPayload(
            model,
            new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = transcript }
            },
            0.2);

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object?> BuildChatPayload(string model, IReadOnlyList<object> messages, double? temperature = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages
        };

        if (temperature.HasValue && SupportsCustomTemperature(model))
            payload["temperature"] = temperature.Value;

        return payload;
    }

    private static bool SupportsCustomTemperature(string? model)
        => string.IsNullOrWhiteSpace(model)
           || !model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase);

    private static ConversacionExtraccionDto? ParseExtraccion(string? json)
    {
        var obj = TryGetJsonObject(json);
        if (obj is null)
            return null;
        var titulo = (obj.Value.TryGetProperty("titulo", out var t) ? t.GetString() ?? "" : "").Trim();
        var descripcion = (obj.Value.TryGetProperty("descripcion", out var d) ? d.GetString() ?? "" : "").Trim();
        if (titulo.Length == 0 && descripcion.Length == 0)
            return null;
        return new ConversacionExtraccionDto { Titulo = titulo, Descripcion = descripcion };
    }

    private static JsonElement? TryGetJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json.Substring(start, end - start + 1));
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildTranscript(IReadOnlyList<ConversacionMensajeDto> mensajes)
    {
        var sb = new StringBuilder();
        foreach (var m in mensajes
                     .Where(x => (x.Direction == "ENTRANTE" || x.Direction == "SALIENTE") && !string.IsNullOrWhiteSpace(x.Texto))
                     .OrderBy(x => x.FechaHora)
                     .TakeLast(40))
        {
            var quien = m.Direction == "ENTRANTE" ? "Cliente" : "Nosotros";
            sb.Append(quien).Append(": ").AppendLine(m.Texto.Trim());
        }
        return sb.ToString().Trim();
    }

    private static ConversacionAnalisisDto? ParseAnalisis(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json.Substring(start, end - start + 1));
            var root = doc.RootElement;
            var sentimiento = (root.TryGetProperty("sentimiento", out var s) ? s.GetString() ?? "" : "").Trim().ToUpperInvariant();
            if (sentimiento is not ("POSITIVO" or "NEGATIVO"))
                sentimiento = "NEUTRO";
            return new ConversacionAnalisisDto
            {
                Resumen = (root.TryGetProperty("resumen", out var r) ? r.GetString() ?? "" : "").Trim(),
                Intencion = (root.TryGetProperty("intencion", out var i) ? i.GetString() ?? "" : "").Trim(),
                Sentimiento = sentimiento
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
