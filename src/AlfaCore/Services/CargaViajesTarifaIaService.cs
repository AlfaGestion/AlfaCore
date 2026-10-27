using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class CargaViajesTarifaIaService(
    IHttpClientFactory httpClientFactory,
    ILogger<CargaViajesTarifaIaService> logger) : ICargaViajesTarifaIaService
{
    public async Task<IReadOnlyList<CargaViajesTarifaImportRow>> ExtraerAsync(IReadOnlyList<CargaViajesTarifaIaImage> imagenes, CancellationToken ct = default)
    {
        var apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty).Trim();
        if (apiKey.Length == 0) throw new InvalidOperationException("El asistente de IA no está configurado en el servidor.");
        if (imagenes.Count == 0) return [];

        var content = new List<object>
        {
            new { type = "text", text = "Extraé las tarifas de estas planillas. Devolvé únicamente JSON válido con esta forma: {\"tarifas\":[{\"nombreLista\":\"\",\"cliente\":\"\",\"chofer\":\"\",\"tipoVehiculo\":\"\",\"destino\":\"\",\"importe\":0,\"tarifaFletero\":false,\"confianza\":\"alta|media|baja\",\"observaciones\":\"\"}]}. Reglas: cada encabezado amarillo (por ejemplo Tarifas Caffaro 2026, Dunlop, J.Matthey, Cincam, Faraway, Combe Power o Necho) define el nombre de la lista y el cliente de todas sus filas; usá Caffaro como cliente y Caffaro 2026 como nombre de lista, sin el prefijo Tarifas. Si la sección no tiene un cliente identificable, cliente debe quedar vacío y corresponde a General; nunca uses General como nombre de cliente. tarifaFletero DEBE ser false para las planillas de clientes y General. Solo puede ser true si la fila pertenece inequívocamente a la planilla de fleteros; no marques todas las filas por la presencia de una planilla de fleteros entre las imágenes. La mayoría no tiene chofer: dejá chofer vacío salvo que aparezca explícitamente. El texto de la primera columna combina vehículo y destino: Cta significa Camioneta; Cta/Wmrt significa Camioneta con destino Walmart; Chasis-Pilar significa Chasis con destino Pilar; Semi Sider/Pilar/La Plata significa Semi Sider con destino Pilar / La Plata; Cta (Reparto/Retiros) significa Camioneta con destino Reparto/Retiros. Si un renglón solo dice Cta, Chasis, Contenedor, Semi Sider, Camioneta o Balancin, destino debe quedar vacío porque es tarifa General. Reconocé también destinos escritos como La Plata, C.A.B.A., G.Bs.As., Berisso/La Plata, Ezeiza y Puerto. No pongas el destino dentro de tipoVehiculo. No inventes importes; si no se leen, usá 0 y explicalo en observaciones." }
        };
        foreach (var image in imagenes)
        {
            if (image.Bytes.Length > 15 * 1024 * 1024) throw new InvalidOperationException($"La imagen '{image.FileName}' supera el límite de 15 MB.");
            var dataUri = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Bytes)}";
            content.Add(new { type = "image_url", image_url = new { url = dataUri, detail = "high" } });
        }

        var request = new
        {
            model = Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() is { Length: > 0 } model ? model : "gpt-4o-mini",
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new[] { new { role = "user", content } }
        };
        using var http = httpClientFactory.CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) { logger.LogError("IA tarifas respondió {Status}: {Body}", response.StatusCode, body); throw new InvalidOperationException("No se pudieron analizar las imágenes con IA."); }

        using var document = JsonDocument.Parse(body);
        var text = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        using var result = JsonDocument.Parse(text);
        if (!result.RootElement.TryGetProperty("tarifas", out var tarifas) || tarifas.ValueKind != JsonValueKind.Array) return [];
        var rows = JsonSerializer.Deserialize<List<CargaViajesTarifaImportRow>>(tarifas.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        foreach (var row in rows)
        {
            row.NombreLista ??= string.Empty;
            row.Cliente ??= string.Empty;
            row.Chofer ??= string.Empty;
            row.TipoVehiculo ??= string.Empty;
            row.Destino ??= string.Empty;
            row.Confianza ??= string.Empty;
            row.Observaciones ??= string.Empty;
            // La clasificación fletero requiere confirmación visual del usuario. La IA no debe
            // propagarla a todas las filas solo porque una de las imágenes pertenece a fleteros.
            row.TarifaFletero = false;
        }
        return rows.OrderBy(x => x.NombreLista, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Cliente, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
