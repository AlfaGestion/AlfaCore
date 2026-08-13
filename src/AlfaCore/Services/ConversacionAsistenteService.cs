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
        string? conocimientoBase = null,
        string? contextoCliente = null,
        CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(mensajeCliente))
            return null;

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var systemPrompt = BuildSystemPrompt(comportamiento, informacion, politica, fueraDeHorario, esUrgente, conocimientoBase, contextoCliente);

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

    public async Task<string?> ResumirAsync(string instrucciones, string contenido, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(contenido))
            return null;

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var payload = new
        {
            model,
            temperature = 0.4,
            messages = new object[]
            {
                new { role = "system", content = string.IsNullOrWhiteSpace(instrucciones) ? "Resumí el siguiente contenido de forma clara y breve." : instrucciones },
                new { role = "user", content = contenido }
            }
        };

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
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
        bool fueraDeHorario, bool esUrgente, string? conocimientoBase, string? contextoCliente)
    {
        var sb = new StringBuilder();
        var comp = (comportamiento ?? string.Empty).Trim();
        sb.AppendLine(comp.Length > 0
            ? comp
            : "Sos un asistente de atención al cliente. Respondés de forma cordial, breve y clara, en español rioplatense.");

        var cliente = (contextoCliente ?? string.Empty).Trim();
        if (cliente.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DATOS DEL CLIENTE (usalos para ajustar y razonar la respuesta según su rubro y prioridad):");
            sb.AppendLine(cliente);
        }

        var info = (informacion ?? string.Empty).Trim();
        if (info.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("INFORMACIÓN DEL NEGOCIO (tu fuente de verdad; priorizala sobre cualquier suposición):");
            sb.AppendLine(info);
        }

        var conocimiento = (conocimientoBase ?? string.Empty).Trim();
        if (conocimiento.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BASE DE CONOCIMIENTO (fragmentos de documentos/instructivos recuperados para esta consulta; usalos como fuente de verdad y no los contradigas):");
            sb.AppendLine(conocimiento);
        }

        sb.AppendLine();
        sb.AppendLine("REGLAS:");
        sb.AppendLine("- Respondé en español rioplatense, breve y directo, sin inventar datos del negocio (precios, stock, plazos) que no estén en la información.");
        sb.AppendLine("- No prometas nada que no puedas sostener con la información dada.");

        switch (NormalizePolitica(politica))
        {
            case "SOLO_INFO":
                sb.AppendLine("- Solo resolvés con la información del negocio/base. Si la consulta NO se puede responder con eso, NO respondas con conocimiento general: pasá a la regla de \"cuando no podés resolver\".");
                break;
            case "GENERAL":
                sb.AppendLine("- Si la información del negocio no alcanza, resolvé igual con tu conocimiento general razonable (tipo RESUELVE).");
                break;
            default: // GENERAL_AVISA
                sb.AppendLine("- Si la información del negocio no alcanza, resolvé con tu conocimiento general PERO aclarando en la respuesta que un asesor lo va a confirmar (tipo RESUELVE).");
                break;
        }

        // Nunca dejar al cliente sin respuesta: si no se puede resolver, o se repregunta (ACLARA) o
        // se manda una contención y se deriva a un humano (DERIVA). Aplica a todas las políticas.
        sb.AppendLine();
        sb.AppendLine("CUANDO NO PODÉS RESOLVER (nunca dejes al cliente sin respuesta):");
        sb.AppendLine("- Si resolviste la consulta: tipo=\"RESUELVE\", \"puede_responder\": true, y la respuesta.");
        sb.AppendLine("- Si NO podés resolver pero UNA pregunta concreta te permitiría hacerlo, y NO se la hiciste ya antes en esta conversación: tipo=\"ACLARA\", \"puede_responder\": false, y en \"respuesta\" esa única pregunta (breve y puntual).");
        sb.AppendLine("- Si NO podés resolver y no hay una pregunta útil, o ya pediste una aclaración antes en esta conversación: tipo=\"DERIVA\", \"puede_responder\": false, y en \"respuesta\" una contención breve del estilo \"Dejame que lo veo con un compañero y te respondo en un ratito 🙂\". No inventes la solución.");
        sb.AppendLine("- Nunca dejes \"respuesta\" vacía.");

        if (fueraDeHorario)
        {
            sb.AppendLine();
            sb.AppendLine("CONTEXTO HORARIO: estás atendiendo FUERA del horario de atención.");
            sb.AppendLine("- Presentate con naturalidad como asistente virtual y avisá que el equipo humano está fuera de horario.");
            sb.AppendLine("- Intentá resolver con la información. Si no podés, decile que dejás su mensaje registrado y que un operador lo revisa apenas sea posible (tipo DERIVA).");
            if (esUrgente)
                sb.AppendLine("- El cliente reporta un problema URGENTE (no puede facturar, o el sistema no anda/ no arranca). Avisale que lo marcaste como URGENTE para que puedan atenderlo a la brevedad, aunque sea fuera de horario.");
        }

        sb.AppendLine();
        sb.AppendLine("Devolvé SOLO un JSON válido con esta forma exacta: {\"tipo\": \"RESUELVE|ACLARA|DERIVA\", \"puede_responder\": true/false, \"respuesta\": \"...\"}.");
        return sb.ToString().Trim();
    }

    private static string NormalizePolitica(string? politica)
    {
        var p = (politica ?? string.Empty).Trim().ToUpperInvariant();
        return p is "SOLO_INFO" or "GENERAL" ? p : "GENERAL_AVISA";
    }

    private static string NormalizeTipo(string? tipo, bool puedeResponder)
    {
        var t = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (t is "RESUELVE" or "ACLARA" or "DERIVA")
            return t;
        return puedeResponder ? "RESUELVE" : "DERIVA";
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
            var tipo = NormalizeTipo(root.TryGetProperty("tipo", out var t) ? t.GetString() : null, puede);

            if (puede && respuesta.Length == 0)
                return null;

            return new ConversacionAsistenteRespuesta { PuedeResponder = puede, Respuesta = respuesta, Tipo = tipo };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
