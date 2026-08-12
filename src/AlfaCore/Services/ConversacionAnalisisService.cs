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
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var transcript = BuildTranscript(mensajes);
        if (transcript.Length == 0)
            return null;

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var payload = new
        {
            model,
            temperature = 0.2,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = """
                        Analizás una conversación de atención al cliente y devolvés SOLO JSON válido
                        con esta forma exacta: {"resumen":"...","intencion":"...","sentimiento":"..."}.
                        - resumen: 1-2 frases, en español rioplatense, qué necesita/pasó.
                        - intencion: etiqueta corta (ej. "Consulta de precio", "Reclamo", "Soporte técnico",
                          "Pedido", "Seguimiento", "Otro").
                        - sentimiento: exactamente uno de POSITIVO, NEUTRO o NEGATIVO (del cliente).
                        No inventes datos que no estén en la conversación.
                        """
                },
                new { role = "user", content = transcript }
            }
        };

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
            var texto = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return ParseAnalisis(texto);
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
