using AlfaCore.Configuration;
using AlfaCore.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class MetaOAuthClient(
    IHttpClientFactory httpClientFactory,
    IWhatsAppCredentialVault credentialVault,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IMetaOAuthClient
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public async Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode)) throw new ArgumentException("El authorization code es obligatorio.", nameof(authorizationCode));
        if (string.IsNullOrWhiteSpace(_options.AppSecret)) throw new InvalidOperationException("Falta configurar el secreto privado de la Meta App.");

        var baseUrl = _options.GraphBaseUrl.TrimEnd('/');
        var version = _options.GraphApiVersion.Trim('/');
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.AppId,
            ["client_secret"] = _options.AppSecret,
            ["code"] = authorizationCode,
            ["redirect_uri"] = string.Empty
        };
        var uri = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString($"{baseUrl}/{version}/oauth/access_token", query);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClientFactory.CreateClient("MetaEmbeddedSignupOAuth").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Meta rechazó el intercambio OAuth ({(int)response.StatusCode}).");

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Meta no devolvió una credencial válida.");
        if (string.IsNullOrWhiteSpace(payload.AccessToken)) throw new InvalidOperationException("Meta no devolvió una credencial válida.");
        DateTime? expiresAt = payload.ExpiresIn is > 0 ? DateTime.UtcNow.AddSeconds(payload.ExpiresIn.Value) : null;
        var reference = await credentialVault.StoreAsync(vaultContext with { ExpiresAtUtc = expiresAt }, payload.AccessToken.AsMemory(), ct);
        return new(reference, expiresAt);
    }

    public async Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret))
            throw new InvalidOperationException("Falta completar la configuración privada de la Meta App.");
        var token = await credentialVault.GetAsync(tokenReference, ct);
        if (token.IsEmpty)
            return new(false, null, []);

        var baseUrl = _options.GraphBaseUrl.TrimEnd('/');
        var version = _options.GraphApiVersion.Trim('/');
        var uri = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString($"{baseUrl}/{version}/debug_token", new Dictionary<string, string?>
        {
            ["input_token"] = token.ToString(),
            ["access_token"] = $"{_options.AppId}|{_options.AppSecret}"
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClientFactory.CreateClient("MetaEmbeddedSignupOAuth").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new MetaWhatsAppManagementException(((int)response.StatusCode).ToString(), (int)response.StatusCode >= 500, response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden, "Meta no pudo validar la credencial autorizada.");
        var payload = await response.Content.ReadFromJsonAsync<DebugTokenResponse>(cancellationToken: ct);
        if (payload?.Data is null) return new(false, null, []);
        DateTime? expiresAt = payload.Data.ExpiresAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(payload.Data.ExpiresAt.Value).UtcDateTime : null;
        return new(payload.Data.IsValid, expiresAt, payload.Data.Scopes ?? []);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    }

    private sealed class DebugTokenResponse
    {
        [JsonPropertyName("data")] public DebugTokenData? Data { get; set; }
    }

    private sealed class DebugTokenData
    {
        [JsonPropertyName("is_valid")] public bool IsValid { get; set; }
        [JsonPropertyName("expires_at")] public long? ExpiresAt { get; set; }
        [JsonPropertyName("scopes")] public string[]? Scopes { get; set; }
    }
}
