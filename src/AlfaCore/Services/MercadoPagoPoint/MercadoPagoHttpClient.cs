using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

/// <summary>Puerto del cliente HTTP original (Newtonsoft.Json.Linq -> System.Text.Json.Nodes,
/// HttpClient propio -> IHttpClientFactory con el cliente nombrado "MercadoPago" registrado en
/// Program.cs). Conserva la misma lógica de reintento con backoff lineal y de parseo de errores.</summary>
public sealed class MercadoPagoHttpClient(IHttpClientFactory httpClientFactory, ILogger<MercadoPagoHttpClient> logger)
{
    private static readonly HttpStatusCode[] RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        (HttpStatusCode)429,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    public Task<JsonObject> GetAsync(string path, string accessToken, CancellationToken ct)
        => SendAsync(HttpMethod.Get, path, accessToken, null, null, ct);

    public Task<JsonObject> PostAsync(string path, string accessToken, JsonObject? body, string? idempotencyKey, CancellationToken ct)
        => SendAsync(HttpMethod.Post, path, accessToken, body, idempotencyKey, ct);

    public async Task<JsonObject> SendAsync(HttpMethod method, string path, string accessToken, JsonObject? body, string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("AccessToken es obligatorio.", nameof(accessToken));

        const int retryCount = 2;

        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.ParseAdd("application/json");

                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                    request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);

                if (body is not null)
                    request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

                var client = httpClientFactory.CreateClient("MercadoPago");
                using var response = await client.SendAsync(request, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                    return string.IsNullOrWhiteSpace(responseText) ? new JsonObject() : JsonNode.Parse(responseText)!.AsObject();

                if (attempt < retryCount && RetryableStatusCodes.Contains(response.StatusCode))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), ct);
                    continue;
                }

                throw BuildApiException(response.StatusCode, responseText);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt < retryCount)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), ct);
                    continue;
                }

                throw new MercadoPagoException("La solicitud a Mercado Pago excedió el timeout configurado.", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < retryCount)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), ct);
                    continue;
                }

                throw new MercadoPagoException("Error de red al comunicarse con Mercado Pago.", ex);
            }
        }

        throw new MercadoPagoException("No fue posible completar la solicitud HTTP.");
    }

    public static string GenerateIdempotencyKey() => Guid.NewGuid().ToString("D");

    private MercadoPagoException BuildApiException(HttpStatusCode statusCode, string responseText)
    {
        var message = ExtractErrorMessage(responseText) ?? "Error al consumir la API de Mercado Pago.";
        logger.LogError("Mercado Pago HTTP error | status={StatusCode} | message={Message}", (int)statusCode, message);
        return new MercadoPagoException(message) { StatusCode = statusCode };
    }

    private static string? ExtractErrorMessage(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        try
        {
            var payload = JsonNode.Parse(responseText)?.AsObject();
            if (payload is null)
                return responseText;

            if (payload["errors"] is JsonArray { Count: > 0 } errors && errors[0] is JsonObject first)
            {
                var message = first.GetStringOrNull("message");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    if (first["details"] is JsonArray { Count: > 0 } details)
                        return message + ": " + details[0];

                    return message;
                }
            }

            var directMessage = payload.GetStringOrNull("message")
                ?? payload.GetStringOrNull("error")
                ?? payload.GetStringOrNull("status_detail");
            return !string.IsNullOrWhiteSpace(directMessage) ? directMessage : responseText;
        }
        catch (JsonException)
        {
            return responseText;
        }
    }
}
