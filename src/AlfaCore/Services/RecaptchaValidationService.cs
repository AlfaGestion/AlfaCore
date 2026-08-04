using System.Net.Http.Json;

namespace AlfaCore.Services;

public interface IRecaptchaValidationService
{
    Task<(bool Success, string Message)> ValidateAsync(string token, string remoteIp, CancellationToken ct = default);
}

public sealed class RecaptchaValidationService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IRecaptchaValidationService
{
    public async Task<(bool Success, string Message)> ValidateAsync(string token, string remoteIp, CancellationToken ct = default)
    {
        var secret = configuration["RegistroPublico:RecaptchaSecret"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secret))
            return (false, "El servidor no tiene configurado el secreto de reCAPTCHA.");

        if (string.IsNullOrWhiteSpace(token))
            return (false, "Completá la validación de reCAPTCHA antes de registrarte.");

        var client = httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = token.Trim(),
            ["remoteip"] = remoteIp?.Trim() ?? string.Empty
        });

        using var response = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify", content, ct);
        if (!response.IsSuccessStatusCode)
            return (false, "No se pudo validar reCAPTCHA en este momento.");

        var payload = await response.Content.ReadFromJsonAsync<RecaptchaVerifyResponse>(cancellationToken: ct);
        if (payload?.Success == true)
            return (true, string.Empty);

        return (false, "La validación de reCAPTCHA no fue aceptada. Intentá nuevamente.");
    }

    private sealed class RecaptchaVerifyResponse
    {
        public bool Success { get; set; }
    }
}
