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
        var expectedAppId = _options.AppId.Trim();
        var expectedCallback = NormalizeCallbackUri(routing.CallbackUrl);

        // No falla antes de intentar reparar sólo porque el primer GET resultó inconcluso (ver
        // incidente Base4264: Meta puede omitir "id" en subscribed_apps para un ítem que sí es
        // nuestro). Confirmar → return; si no, (re)suscribir de forma idempotente y volver a
        // verificar; recién ahí, si sigue sin poder confirmarse, falla controlado.
        var first = await GetSubscribedAppsAsync(tokenReference, normalizedWabaId, ct);
        if (IsOurSubscriptionConfirmed(first.Items, expectedAppId, expectedCallback))
            return;

        using var subscribe = await CreateRequestAsync(HttpMethod.Post, $"{normalizedWabaId}/subscribed_apps", tokenReference, ct);
        subscribe.Content = JsonContent.Create(new { override_callback_uri = routing.CallbackUrl, verify_token = routing.VerifyToken });
        await SendSuccessAsync(subscribe, ct);

        var verified = await GetSubscribedAppsAsync(tokenReference, normalizedWabaId, ct);
        if (IsOurSubscriptionConfirmed(verified.Items, expectedAppId, expectedCallback))
            return;

        if (verified.IsUnparseable)
            throw new MetaWhatsAppManagementException("META_SUBSCRIBED_APPS_UNPARSEABLE", false, false,
                $"No se pudo determinar de forma segura el estado de suscripción del WABA tras reparar: {verified.MalformedCount} de {verified.RawCount} elemento(s) de subscribed_apps sin evidencia utilizable.");
        throw new MetaWhatsAppManagementException("META_CALLBACK_ROUTING_MISMATCH", false, false, "Meta no confirmó el callback correspondiente a la base.");
    }

    /// <summary>
    /// ¿Alguno de los ítems constituye evidencia válida de que <paramref name="expectedAppId"/> está
    /// suscripta con el callback esperado?
    ///  1. id presente y parseable == expectedAppId, con el callback correcto → sí.
    ///  2. id presente y parseable pero de OTRA app → nunca cuenta, aunque su callback coincidiera
    ///     (nunca se usa el callback de un id explícito distinto como evidencia propia).
    ///  3. id ausente (Meta lo omitió, visto en el incidente Base4264) con override_callback_uri ==
    ///     al esperado → evidencia secundaria válida.
    ///  4. cualquier otro caso → no concluyente para este ítem.
    /// </summary>
    private static bool IsOurSubscriptionConfirmed(IReadOnlyList<WabaSubscriptionItem> items, string expectedAppId, string expectedCallback)
    {
        foreach (var item in items)
        {
            if (item.AppId is not null)
            {
                if (string.Equals(item.AppId, expectedAppId, StringComparison.Ordinal)
                    && string.Equals(NormalizeCallbackUri(item.OverrideCallbackUrl), expectedCallback, StringComparison.Ordinal))
                    return true;
                continue;
            }
            if (string.Equals(NormalizeCallbackUri(item.OverrideCallbackUrl), expectedCallback, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Normaliza una URI de callback para comparación segura: canonicaliza esquema/host (minúsculas,
    /// vía System.Uri) y recorta un único trailing slash del path si no es la raíz. Si no es una URI
    /// absoluta válida, cae a un trim simple — nunca lanza, nunca inventa una URI.
    /// </summary>
    private static string NormalizeCallbackUri(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed.TrimEnd('/');
        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;
        return uri.GetLeftPart(UriPartial.Authority) + path + uri.Query;
    }

    private sealed record WabaSubscriptionItem(string? AppId, string OverrideCallbackUrl);

    private readonly record struct SubscribedAppsResult(IReadOnlyList<WabaSubscriptionItem> Items, int RawCount, int MalformedCount)
    {
        public bool IsUnparseable => RawCount > 0 && Items.Count == 0;
    }

    /// <summary>
    /// Lectura tolerante de <c>{wabaId}/subscribed_apps</c>: a diferencia de <see cref="GetPagedAsync{T}"/>,
    /// un ítem individual sin "id" utilizable NO aborta toda la lectura ni se descarta automáticamente
    /// como malformado — si trae <c>override_callback_uri</c> se conserva como evidencia secundaria (ver
    /// <see cref="IsOurSubscriptionConfirmed"/>; incidente Base4264, Meta omitió "id" en un ítem real).
    /// Sólo se registra diagnóstico sanitizado (sin token, sin body completo) para ítems sin NINGUNA
    /// evidencia utilizable (ni id ni callback). Nunca lanza acá — <see cref="EnsureWabaSubscriptionAsync"/>
    /// decide si falla, después de intentar reparar. Los errores HTTP reales de Meta (permisos, rate
    /// limit, etc.) no pasan por esta tolerancia: <see cref="SendJsonAsync"/> los sigue lanzando tal cual.
    /// </summary>
    private async Task<SubscribedAppsResult> GetSubscribedAppsAsync(WhatsAppCredentialReference tokenReference, string wabaId, CancellationToken ct)
    {
        var result = new List<WabaSubscriptionItem>();
        var rawCount = 0;
        var malformedCount = 0;
        string? next = BuildGraphUri($"{wabaId}/subscribed_apps?fields={Uri.EscapeDataString("id,override_callback_uri")}&limit=100").ToString();
        while (!string.IsNullOrWhiteSpace(next))
        {
            using var request = await CreateAbsoluteRequestAsync(HttpMethod.Get, next, tokenReference, ct);
            using var document = await SendJsonAsync(request, ct);
            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                var itemIndex = 0;
                foreach (var item in data.EnumerateArray())
                {
                    rawCount++;
                    var callbackUri = GetString(item, "override_callback_uri");
                    if (TryParseMetaId(item, "id", out var id, out var idKind, out var idPresent))
                    {
                        result.Add(new WabaSubscriptionItem(id, callbackUri));
                    }
                    else if (!idPresent && callbackUri.Length > 0)
                    {
                        // "id" ausente (no simplemente no parseable) pero con callback utilizable:
                        // evidencia secundaria, no un ítem malformado.
                        result.Add(new WabaSubscriptionItem(null, callbackUri));
                    }
                    else
                    {
                        malformedCount++;
                        TryWriteMetaAssetParseDiagnostic("subscribed_apps", wabaId, itemIndex, item, idKind, idPresent);
                    }
                    itemIndex++;
                }
            }
            next = document.RootElement.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("next", out var nextElement)
                && nextElement.ValueKind == JsonValueKind.String
                ? nextElement.GetString()
                : null;
        }

        return new SubscribedAppsResult(result, rawCount, malformedCount);
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

    public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedWabaId = RequiredMetaId(wabaId, nameof(wabaId));
        return GetPagedAsync(tokenReference, $"{normalizedWabaId}/phone_numbers",
            "id,display_phone_number,verified_name,quality_rating,platform_type,is_on_biz_app",
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
                    MapRegistrationStatus(platformType),
                    GetBool(item, "is_on_biz_app"));
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

    /// <summary>
    /// POST /{phoneNumberId}/smb_app_data { messaging_product:"whatsapp", sync_type }. Sólo soporta
    /// sync_type=history y sync_type=smb_app_state_sync -- cualquier otro valor es un error de
    /// programación (nunca llega desde Meta). request_id se captura best-effort: si Meta no lo
    /// devuelve (o lo devuelve en una forma no reconocida), RequestId queda vacío y la llamada NO se
    /// considera fallida -- SendJsonAsync ya lanzó si el POST no fue exitoso.
    /// </summary>
    public async Task<MetaSmbAppDataSyncResult> RequestSmbAppDataSyncAsync(string phoneNumberId, WhatsAppCoexistenceSyncType syncType, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
    {
        var normalizedPhoneId = RequiredMetaId(phoneNumberId, nameof(phoneNumberId));
        var syncTypeValue = syncType switch
        {
            WhatsAppCoexistenceSyncType.History => "history",
            WhatsAppCoexistenceSyncType.ContactState => "smb_app_state_sync",
            _ => throw new ArgumentOutOfRangeException(nameof(syncType), syncType, "sync_type no soportado.")
        };

        using var request = await CreateRequestAsync(HttpMethod.Post, $"{normalizedPhoneId}/smb_app_data", tokenReference, ct);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", sync_type = syncTypeValue });
        using var document = await SendJsonAsync(request, ct);

        var requestId = GetString(document.RootElement, "request_id");
        if (requestId.Length == 0
            && document.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0)
            requestId = GetString(data[0], "request_id");

        return new MetaSmbAppDataSyncResult(requestId);
    }

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
        string? subcode = null;
        string? errorType = null;
        string? errorMessage = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var errorCode))
                    code = errorCode.ToString();
                if (error.TryGetProperty("error_subcode", out var errorSubcode))
                    subcode = errorSubcode.ToString();
                if (error.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                    errorType = typeElement.GetString();
                if (error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                    errorMessage = SanitizeMetaMessage(messageElement.GetString());
            }
        }
        catch { }
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var businessUsage = TryReadBusinessUseCaseUsage(response);
        var reauth = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || code is "190" or "10";
        var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests
            || retryAfter.HasValue || businessUsage.EstimatedTimeToRegainAccess.HasValue
            || code is "1" or "2" or "4" or "17" or "32" or "613" or "80008";
        // No ocultamos el error real de Meta: HTTP/code/subcode/type/message (sanitizado) quedan en
        // la excepción para quien la capture, aunque hoy sólo se persista ErrorCode + step + incidente.
        return new MetaWhatsAppManagementException(code, transient, reauth, "Meta no pudo completar la operación de administración de WhatsApp.",
            null, subcode, (int)response.StatusCode, retryAfter, businessUsage.HeaderPresent, businessUsage.EstimatedTimeToRegainAccess,
            errorType, errorMessage);
    }

    private static string? SanitizeMetaMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        var cleaned = new string(message.Where(static c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length <= 300 ? cleaned : cleaned[..300];
    }

    /// <summary>
    /// Diagnóstico best-effort para un ítem de un listado de Meta (p. ej. subscribed_apps) cuyo "id"
    /// no se pudo determinar de forma segura. Nunca token, nunca el body completo: sólo el endpoint
    /// lógico, el WABA (no es secreto), el índice del ítem, los NOMBRES de propiedad presentes (no sus
    /// valores) y el JsonValueKind de "id". Nunca altera el resultado real de la operación con Meta.
    /// </summary>
    private static void TryWriteMetaAssetParseDiagnostic(
        string endpointLogico, string wabaId, int itemIndex, JsonElement item, JsonValueKind idValueKind, bool idPresent)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "diagnostics");
            Directory.CreateDirectory(directory);
            var propertyNames = item.ValueKind == JsonValueKind.Object
                ? item.EnumerateObject().Select(static p => p.Name).ToArray()
                : Array.Empty<string>();
            var record = new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Endpoint = endpointLogico,
                WabaId = wabaId,
                ItemIndex = itemIndex,
                PropertyNamesPresent = propertyNames,
                IdValueKind = idValueKind.ToString(),
                IdPresent = idPresent
            };
            var path = Path.Combine(directory, $"meta-asset-parse-failures-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // El diagnóstico nunca debe enmascarar ni alterar el resultado real de la operación con Meta.
        }
    }

    /// <summary>
    /// Parser tolerante del "id" de un ítem de Graph: acepta JSON string numérico (formato histórico)
    /// O JSON number entero positivo (algunos edges de Graph devuelven ids como número), y normaliza
    /// ambos a string. Cualquier otra forma (ausente, no numérico, negativo, no entero) se reporta como
    /// no utilizable — nunca se inventa un id ni se asume éxito.
    /// </summary>
    // internal (no private) únicamente para poder testear directamente la precisión exacta del
    // parseo de ids grandes (ver AlfaCore.Tests.MetaWhatsAppManagementClientTests); no forma parte
    // de la API pública del cliente.
    internal static bool TryParseMetaId(JsonElement item, string propertyName, out string id, out JsonValueKind valueKind, out bool present)
    {
        id = string.Empty;
        valueKind = JsonValueKind.Undefined;
        present = item.ValueKind == JsonValueKind.Object && item.TryGetProperty(propertyName, out var value);
        if (!present)
            return false;

        value = item.GetProperty(propertyName);
        valueKind = value.ValueKind;
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = (value.GetString() ?? string.Empty).Trim();
                if (text.Length > 0 && text.All(char.IsDigit))
                {
                    id = text;
                    return true;
                }
                return false;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var number) && number > 0)
                {
                    id = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static (bool HeaderPresent, TimeSpan? EstimatedTimeToRegainAccess) TryReadBusinessUseCaseUsage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Business-Use-Case-Usage", out var values))
            return (false, null);

        var raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return (true, null);

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (TryFindEstimatedSeconds(document.RootElement, out var seconds) && seconds >= 0)
                return (true, TimeSpan.FromSeconds(seconds));
        }
        catch (JsonException) { }

        return (true, null);
    }

    private static bool TryFindEstimatedSeconds(JsonElement element, out double seconds)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "estimated_time_to_regain_access", StringComparison.OrdinalIgnoreCase)
                    && property.Value.TryGetDouble(out seconds))
                    return true;
                if (TryFindEstimatedSeconds(property.Value, out seconds))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (TryFindEstimatedSeconds(item, out seconds))
                    return true;
        }

        seconds = 0;
        return false;
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
    {
        // Acepta "id" como JSON string numérico o como JSON number entero positivo (ver TryParseMetaId).
        if (TryParseMetaId(item, "id", out var id, out _, out _))
            return id;
        throw new MetaWhatsAppManagementException("META_INVALID_ASSET", false, false, $"Meta devolvió un identificador inválido para {label}.");
    }

    private static string RequiredMetaId(string? value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Any(c => !char.IsDigit(c)))
            throw new MetaWhatsAppManagementException("META_INVALID_ASSET", false, false, $"Meta devolvió un identificador inválido para {parameterName}.");
        return normalized;
    }

    private static string GetString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;

    private static bool GetBool(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
}
