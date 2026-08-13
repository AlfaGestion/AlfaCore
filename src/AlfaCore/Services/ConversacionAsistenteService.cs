using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class ConversacionAsistenteService(IHttpClientFactory httpClientFactory) : IConversacionAsistenteService
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    public async Task<ConversacionAsistenteRespuesta?> ResponderAsync(
        string comportamiento,
        string informacion,
        string politica,
        string mensajeCliente,
        IReadOnlyList<ConversacionMensajeDto> historial,
        bool fueraDeHorario = false,
        bool esUrgente = false,
        CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(mensajeCliente))
            return null;

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var systemPrompt = BuildSystemPrompt(comportamiento, informacion, politica, fueraDeHorario, esUrgente);

        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var m in historial
                     .Where(x => (x.Direction == "ENTRANTE" || x.Direction == "SALIENTE") && !string.IsNullOrWhiteSpace(x.Texto))
                     .OrderBy(x => x.FechaHora)
                     .TakeLast(30))
        {
            messages.Add(new
            {
                role = m.Direction == "ENTRANTE" ? "user" : "assistant",
                content = m.Texto.Trim()
            });
        }
        messages.Add(new { role = "user", content = mensajeCliente.Trim() });

        var payload = new
        {
            model,
            temperature = 0.3,
            response_format = new { type = "json_object" },
            messages
        };

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var texto = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return ParseRespuesta(texto);
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

    private static string BuildSystemPrompt(string comportamiento, string informacion, string politica,
        bool fueraDeHorario, bool esUrgente)
    {
        var sb = new StringBuilder();
        var comp = (comportamiento ?? string.Empty).Trim();
        sb.AppendLine(comp.Length > 0
            ? comp
            : "Sos un asistente de atención al cliente. Respondés de forma cordial, breve y clara, en español rioplatense.");

        var info = (informacion ?? string.Empty).Trim();
        if (info.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("INFORMACIÓN DEL NEGOCIO (tu fuente de verdad; priorizala sobre cualquier suposición):");
            sb.AppendLine(info);
        }

        sb.AppendLine();
        sb.AppendLine("REGLAS:");
        sb.AppendLine("- Respondé en español rioplatense, breve y directo, sin inventar datos del negocio (precios, stock, plazos) que no estén en la información.");
        sb.AppendLine("- No prometas nada que no puedas sostener con la información dada.");

        switch (NormalizePolitica(politica))
        {
            case "SOLO_INFO":
                sb.AppendLine("- Si la consulta NO se puede responder con la información del negocio, poné \"puede_responder\": false y dejá \"respuesta\" vacía: se derivará a una persona. No respondas con conocimiento general.");
                break;
            case "GENERAL":
                sb.AppendLine("- Si la información del negocio no alcanza, respondé igual con tu conocimiento general razonable. Poné \"puede_responder\": true salvo que la consulta sea incomprensible.");
                break;
            default: // GENERAL_AVISA
                sb.AppendLine("- Si la información del negocio no alcanza, respondé con tu conocimiento general PERO aclarando en la respuesta que un asesor lo va a confirmar. Poné \"puede_responder\": true salvo que la consulta sea incomprensible.");
                break;
        }

        if (fueraDeHorario)
        {
            sb.AppendLine();
            sb.AppendLine("CONTEXTO HORARIO: estás atendiendo FUERA del horario de atención.");
            sb.AppendLine("- Presentate con naturalidad como asistente virtual y avisá que el equipo humano está fuera de horario.");
            sb.AppendLine("- Intentá resolver con la información. Si no podés, decile que dejás su mensaje registrado y que un operador lo revisa apenas sea posible.");
            sb.AppendLine("- Siempre poné \"puede_responder\": true: aunque no resuelvas, respondé con la contención (nunca dejes al cliente sin respuesta fuera de horario).");
            if (esUrgente)
                sb.AppendLine("- El cliente reporta un problema URGENTE (no puede facturar, o el sistema no anda/ no arranca). Avisale que lo marcaste como URGENTE para que puedan atenderlo a la brevedad, aunque sea fuera de horario.");
        }

        sb.AppendLine();
        sb.AppendLine("Devolvé SOLO un JSON válido con esta forma exacta: {\"puede_responder\": true/false, \"respuesta\": \"...\"}.");
        return sb.ToString().Trim();
    }

    private static string NormalizePolitica(string? politica)
    {
        var p = (politica ?? string.Empty).Trim().ToUpperInvariant();
        return p is "SOLO_INFO" or "GENERAL" ? p : "GENERAL_AVISA";
    }

    private static ConversacionAsistenteRespuesta? ParseRespuesta(string? json)
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
            var puede = root.TryGetProperty("puede_responder", out var p)
                && (p.ValueKind == JsonValueKind.True
                    || (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var b) && b));
            var respuesta = (root.TryGetProperty("respuesta", out var r) ? r.GetString() ?? string.Empty : string.Empty).Trim();

            if (puede && respuesta.Length == 0)
                return null;

            return new ConversacionAsistenteRespuesta { PuedeResponder = puede, Respuesta = respuesta };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
