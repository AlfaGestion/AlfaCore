using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class MetaWhatsAppManagementClient(
    IHttpClientFactory httpClientFactory,
    IWhatsAppCredentialVault credentialVault,
    IWhatsAppPhonePinVault pinVault,
    IWhatsAppWabaRoutingProvider routingProvider,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IMetaWhatsAppManagementClient
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
        => GetPagedAsync(tokenReference, "me/businesses", "id,name", item =>
            new MetaAuthorizedBusiness(RequiredId(item, "business"), GetString(item, "name")), ct);

    public async Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedBusinessId = RequiredMetaId(businessId, nameof(businessId));
        var owned = await GetPagedAsync(tokenReference, $"{normalizedBusinessId}/owned_whatsapp_business_accounts", "id,name", item =>
            new MetaWabaAsset(RequiredId(item, "WABA"), normalizedBusinessId, GetString(item, "name")), ct);
        var client = await GetPagedAsync(tokenReference, $"{normalizedBusinessId}/client_whatsapp_business_accounts", "id,name", item =>
            new MetaWabaAsset(RequiredId(item, "WABA"), normalizedBusinessId, GetString(item, "name")), ct, allowUnsupportedEdge: true);
        return owned.Concat(client).GroupBy(x => x.WabaId, StringComparer.Ordinal).Select(x => x.First()).ToArray();
    }

    public async Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedWabaId = RequiredMetaId(wabaId, nameof(wabaId));
        IReadOnlyList<string> assigned;
        try
        {
            assigned = await GetPagedAsync(tokenReference, $"{normalizedWabaId}/assigned_users", "id", item => RequiredId(item, "usuario de sistema"), ct);
        }
        catch (MetaWhatsAppManagementException ex) when (ex.ErrorCode is "100" or "2500")
        {
            await EnsureAssetReadableAsync(normalizedWabaId, tokenReference, ct);
            return;
        }
        if (assigned.Contains(_options.SystemUserId.Trim(), StringComparer.Ordinal))
            return;

        using var request = await CreateRequestAsync(HttpMethod.Post, $"{normalizedWabaId}/assigned_users", tokenReference, ct);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["user"] = RequiredMetaId(_options.SystemUserId, nameof(_options.SystemUserId)),
            ["tasks"] = "[\"MANAGE\"]"
        });
        await SendSuccessAsync(request, ct);
    }

    public async Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedWabaId = RequiredMetaId(wabaId, nameof(wabaId));
        var routing = await routingProvider.GetAsync(idBase, ct);
        await VerifyCallbackAsync(routing, ct);
        var subscriptions = await GetPagedAsync(tokenReference, $"{normalizedWabaId}/subscribed_apps", "id,override_callback_uri", item =>
            new WabaSubscription(RequiredId(item, "aplicación"), GetString(item, "override_callback_uri")), ct);
        var current = subscriptions.SingleOrDefault(x => string.Equals(x.AppId, _options.AppId.Trim(), StringComparison.Ordinal));
        if (current is null)
        {
            using var subscribe = await CreateRequestAsync(HttpMethod.Post, $"{normalizedWabaId}/subscribed_apps", tokenReference, ct);
            await SendSuccessAsync(subscribe, ct);
        }
        if (string.Equals(current?.OverrideCallbackUrl?.TrimEnd('/'), routing.CallbackUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return;
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{normalizedWabaId}/subscribed_apps", tokenReference, ct);
        request.Content = JsonContent.Create(new { override_callback_uri = routing.CallbackUrl, verify_token = routing.VerifyToken });
        await SendSuccessAsync(request, ct);
        var verified = await GetPagedAsync(tokenReference, $"{normalizedWabaId}/subscribed_apps", "id,override_callback_uri", item =>
            new WabaSubscription(RequiredId(item, "aplicación"), GetString(item, "override_callback_uri")), ct);
        if (!verified.Any(x => x.AppId == _options.AppId.Trim() && string.Equals(x.OverrideCallbackUrl.TrimEnd('/'), routing.CallbackUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            throw new MetaWhatsAppManagementException("META_CALLBACK_ROUTING_MISMATCH", false, false, "Meta no confirmó el callback correspondiente a la base.");
    }

    private async Task VerifyCallbackAsync(WhatsAppWabaRoutingConfiguration routing, CancellationToken ct)
    {
        var challenge = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
        var separator = routing.CallbackUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = $"{routing.CallbackUrl}{separator}hub.mode=subscribe&hub.verify_token={Uri.EscapeDataString(routing.VerifyToken)}&hub.challenge={challenge}";
        using var response = await httpClientFactory.CreateClient("MetaEmbeddedSignupManagement").GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode || !string.Equals(body.Trim(), challenge, StringComparison.Ordinal))
            throw new MetaWhatsAppManagementException("CALLBACK_VERIFICATION_FAILED", false, false, "El callback público de la base no superó la verificación.");
    }

    private sealed record WabaSubscription(string AppId, string OverrideCallbackUrl);

    public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedWabaId = RequiredMetaId(wabaId, nameof(wabaId));
        return GetPagedAsync(tokenReference, $"{normalizedWabaId}/phone_numbers",
            "id,display_phone_number,verified_name,quality_rating,platform_type",
            item =>
            {
                var platformType = GetString(item, "platform_type");
                return new MetaPhoneAsset(
                    RequiredId(item, "número"),
                    normalizedWabaId,
                    GetString(item, "display_phone_number"),
                    GetString(item, "verified_name"),
                    platformType,
                    GetString(item, "quality_rating"),
                    MapRegistrationStatus(platformType));
            }, ct);
    }

    public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedWabaId = RequiredMetaId(wabaId, nameof(wabaId));
        return GetPagedAsync(tokenReference, $"{normalizedWabaId}/message_templates", "id,name,language,status,category,components", MapTemplate, ct);
    }

    public async Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedPhoneId = RequiredMetaId(phoneNumberId, nameof(phoneNumberId));
        using var request = await CreateRequestAsync(HttpMethod.Get, $"{normalizedPhoneId}?fields=platform_type", tokenReference, ct);
        using var document = await SendJsonAsync(request, ct);
        return MapRegistrationStatus(GetString(document.RootElement, "platform_type"));
    }

    public async Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pinReference, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedPhoneId = RequiredMetaId(phoneNumberId, nameof(phoneNumberId));
        var pin = await pinVault.GetAsync(pinReference, ct);
        if (pin.Length != 6 || pin.Span.ToArray().Any(static value => value is < '0' or > '9'))
            throw new MetaWhatsAppManagementException("META_INVALID_PIN", false, false, "El PIN protegido no cumple el contrato de Meta.");

        using var request = await CreateRequestAsync(HttpMethod.Post, $"{normalizedPhoneId}/register", tokenReference, ct);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["messaging_product"] = "whatsapp",
            ["pin"] = pin.ToString()
        });
        await SendSuccessAsync(request, ct);
    }

    public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
        => Task.FromResult(MetaCustomerPaymentReadiness.Unknown);

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(WhatsAppCredentialReference tokenReference, string path, string fields, Func<JsonElement, T> map, CancellationToken ct, bool allowUnsupportedEdge = false)
    {
        var result = new List<T>();
        string? next = BuildGraphUri($"{path}?fields={Uri.EscapeDataString(fields)}&limit=100").ToString();
        while (!string.IsNullOrWhiteSpace(next))
        {
            using var request = await CreateAbsoluteRequestAsync(HttpMethod.Get, next, tokenReference, ct);
            JsonDocument document;
            try { document = await SendJsonAsync(request, ct); }
            catch (MetaWhatsAppManagementException ex) when (allowUnsupportedEdge && ex.ErrorCode is "100" or "2500") { return result; }
            using (document)
            {
                if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var item in data.EnumerateArray()) result.Add(map(item));
                next = document.RootElement.TryGetProperty("paging", out var paging)
                    && paging.TryGetProperty("next", out var nextElement)
                    && nextElement.ValueKind == JsonValueKind.String
                    ? nextElement.GetString()
                    : null;
            }
        }
        return result;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relativePath, WhatsAppCredentialReference tokenReference, CancellationToken ct)
        => await CreateAbsoluteRequestAsync(method, BuildGraphUri(relativePath).ToString(), tokenReference, ct);

    private async Task<HttpRequestMessage> CreateAbsoluteRequestAsync(HttpMethod method, string uri, WhatsAppCredentialReference tokenReference, CancellationToken ct)
    {
        var token = await credentialVault.GetAsync(tokenReference, ct);
        if (token.IsEmpty) throw new MetaWhatsAppManagementException("META_AUTH_EXPIRED", false, true, "La credencial de Meta no está disponible.");
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Uri BuildGraphUri(string relativePath)
    {
        var baseUrl = _options.GraphBaseUrl.TrimEnd('/');
        var version = _options.GraphApiVersion.Trim('/');
        return new Uri($"{baseUrl}/{version}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private async Task SendSuccessAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await httpClientFactory.CreateClient("MetaEmbeddedSignupManagement").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, ct);
    }

    private async Task EnsureAssetReadableAsync(string assetId, WhatsAppCredentialReference tokenReference, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"{assetId}?fields=id", tokenReference, ct);
        using var document = await SendJsonAsync(request, ct);
        if (!string.Equals(GetString(document.RootElement, "id"), assetId, StringComparison.Ordinal))
            throw new MetaWhatsAppManagementException("META_PERMISSION_DENIED", false, true, "La credencial no tiene acceso al recurso de WhatsApp autorizado.");
    }

    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await httpClientFactory.CreateClient("MetaEmbeddedSignupManagement").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static async Task<MetaWhatsAppManagementException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string code = ((int)response.StatusCode).ToString();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var errorCode))
                code = errorCode.ToString();
        }
        catch { }
        var reauth = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || code is "190" or "10";
        var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests || code is "1" or "2" or "4" or "17" or "32" or "613";
        return new MetaWhatsAppManagementException(code, transient, reauth, "Meta no pudo completar la operación de administración de WhatsApp.");
    }

    private static MetaPhoneRegistrationStatus MapRegistrationStatus(string platformType)
        => platformType.Trim().ToUpperInvariant() switch
        {
            "CLOUD_API" => MetaPhoneRegistrationStatus.Registered,
            "NOT_APPLICABLE" or "UNDEFINED" or "" => MetaPhoneRegistrationStatus.RegistrationRequired,
            _ => MetaPhoneRegistrationStatus.Unknown
        };

    private static MetaMessageTemplate MapTemplate(JsonElement item)
    {
        var header = string.Empty; var body = string.Empty; var footer = string.Empty;
        if (item.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            foreach (var component in components.EnumerateArray())
            {
                var type = GetString(component, "type").ToUpperInvariant();
                if (type == "HEADER") header = GetString(component, "text");
                else if (type == "BODY") body = GetString(component, "text");
                else if (type == "FOOTER") footer = GetString(component, "text");
            }
        return new(RequiredId(item, "plantilla"), GetString(item, "name"), GetString(item, "language"),
            GetString(item, "status"), GetString(item, "category"), header, body, footer);
    }

    private static string RequiredId(JsonElement item, string label)
        => RequiredMetaId(GetString(item, "id"), label);

    private static string RequiredMetaId(string? value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Any(c => !char.IsDigit(c)))
            throw new MetaWhatsAppManagementException("META_INVALID_ASSET", false, false, $"Meta devolvió un identificador inválido para {parameterName}.");
        return normalized;
    }

    private static string GetString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
}
