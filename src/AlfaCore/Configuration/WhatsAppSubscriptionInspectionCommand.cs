using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot 100% READ-ONLY para diagnosticar el paso SUBSCRIBING_WABAS sin ejecutar el POST de
/// suscripcion. Reproduce las dos lecturas previas del flujo real: resolucion de routing + self-check
/// del callback publico, y GET /{wabaId}/subscribed_apps con la credencial runtime del numero.
/// </summary>
internal static class WhatsAppSubscriptionInspectionCommand
{
    public const string Verb = "--inspect-whatsapp-subscription";

    private const string EvidenceRoutingConfigurationFailed = "ROUTING_CONFIGURATION_FAILED";
    private const string EvidenceCallbackSelfCheckFailed = "CALLBACK_SELF_CHECK_FAILED";
    private const string EvidenceAppAlreadySubscribed = "APP_ALREADY_SUBSCRIBED";
    private const string EvidenceAppNotSubscribed = "APP_NOT_SUBSCRIBED";
    private const string EvidenceGraphSubscribedAppsFailed = "GRAPH_SUBSCRIBED_APPS_FAILED";
    private const string EvidenceNoFailureReproduced = "NO_FAILURE_REPRODUCED_READ_ONLY";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IConfiguration configuration, TextWriter output, CancellationToken ct)
    {
        var idBaseArg = ReadOption(args, "--id-base");
        var phoneNumberId = ReadOption(args, "--phone-number-id")?.Trim();
        var wabaId = ReadOption(args, "--waba-id")?.Trim();
        if (!int.TryParse(idBaseArg, out var idBase) || idBase <= 0 || string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(wabaId))
        {
            output.WriteLine("Uso: AlfaCore --inspect-whatsapp-subscription --id-base <idBase> --phone-number-id <phoneNumberId> --waba-id <wabaId>");
            return 1;
        }

        var embeddedOptions = configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName).Get<WhatsAppEmbeddedSignupOptions>() ?? new();
        var optionsWrapper = Options.Create(embeddedOptions);
        var whatsAppOptions = configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new();
        var centralBases = new ReadOnlyCentralBasesService(configuration);
        var session = new SessionService(new OneShotConexionClienteService());
        var configService = new ConversacionesConfigService(
            configuration,
            session,
            new NullAppEventService(),
            Options.Create(whatsAppOptions),
            new SingleClientFactory(new HttpClient()),
            new OneShotAppUserSessionService(),
            new AllowAllConversacionesAuthorizationService(),
            centralBases);
        IWhatsAppWabaRoutingProvider routingProvider = new WhatsAppWabaRoutingProvider(centralBases, session, configService, optionsWrapper);
        IWhatsAppAssetOwnershipStore ownershipStore = new WhatsAppAssetOwnershipStore(configuration);
        IWhatsAppCredentialVault credentialVault = new WhatsAppSecureVault(configuration, optionsWrapper);
        IWhatsAppRuntimeCredentialResolver resolver = new WhatsAppRuntimeCredentialResolver(ownershipStore, credentialVault, optionsWrapper);
        using var httpClient = new HttpClient();

        return await ExecuteAsync(
            idBase,
            phoneNumberId,
            wabaId,
            embeddedOptions.AppId,
            ownershipStore,
            resolver,
            routingProvider,
            httpClient,
            embeddedOptions.GraphBaseUrl,
            output,
            ct);
    }

    internal static async Task<int> ExecuteAsync(
        int idBase,
        string phoneNumberId,
        string wabaId,
        string expectedAppId,
        IWhatsAppAssetOwnershipStore ownershipStore,
        IWhatsAppRuntimeCredentialResolver credentialResolver,
        IWhatsAppWabaRoutingProvider routingProvider,
        HttpClient httpClient,
        string graphBaseUrl,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine("== inspect-whatsapp-subscription (read-only) ==");
        output.WriteLine($"BASE = {idBase}");
        output.WriteLine($"PHONE_NUMBER_ID = {phoneNumberId}");
        output.WriteLine($"WABA_ID = {wabaId}");
        output.WriteLine("");

        var normalizedWabaId = (wabaId ?? string.Empty).Trim();
        var normalizedPhoneNumberId = (phoneNumberId ?? string.Empty).Trim();
        var normalizedExpectedAppId = (expectedAppId ?? string.Empty).Trim();

        WhatsAppPhoneOwnership? ownership;
        try
        {
            if (!await ownershipStore.IsSchemaAvailableAsync(ct))
            {
                output.WriteLine("OWNERSHIP = ERROR (esquema central no disponible)");
                WriteOwnershipBlocked(output, normalizedExpectedAppId);
                return 1;
            }

            ownership = await ownershipStore.GetPhoneOwnershipAsync(normalizedPhoneNumberId, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"OWNERSHIP = ERROR ({ex.GetType().Name})");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (ownership is null)
        {
            output.WriteLine("OWNERSHIP = ERROR (sin ownership central para este PhoneNumberId)");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (ownership.IdBase != idBase)
        {
            output.WriteLine("OWNERSHIP = ERROR (PhoneNumberId pertenece a otra base)");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (!string.Equals(ownership.WabaId, normalizedWabaId, StringComparison.Ordinal))
        {
            output.WriteLine("OWNERSHIP = ERROR (WabaId no coincide con el ownership del PhoneNumberId)");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        output.WriteLine("OWNERSHIP = OK");

        WhatsAppRuntimeCredential credential;
        try
        {
            credential = await credentialResolver.ResolveAsync(idBase, null, normalizedPhoneNumberId, new ConversacionWhatsAppConfigDto(), ct);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WhatsAppEmbeddedVaultUnavailableException or WhatsAppEmbeddedSchemaUnavailableException)
        {
            output.WriteLine($"VAULT_CREDENTIAL = ERROR ({ex.GetType().Name})");
            WriteCredentialBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (credential.Origin != WhatsAppRuntimeCredentialOrigin.EmbeddedSignup
            || !string.Equals(credential.PhoneNumberId, normalizedPhoneNumberId, StringComparison.Ordinal)
            || !string.Equals(credential.WabaId, normalizedWabaId, StringComparison.Ordinal))
        {
            output.WriteLine("VAULT_CREDENTIAL = ERROR (la credencial runtime no coincide con ownership)");
            WriteCredentialBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        output.WriteLine("VAULT_CREDENTIAL = OK");
        output.WriteLine("");

        var callback = await InspectCallbackAsync(idBase, routingProvider, httpClient, output, ct);
        output.WriteLine("");
        var subscribedApps = await InspectSubscribedAppsAsync(
            httpClient,
            graphBaseUrl,
            credential,
            normalizedWabaId,
            normalizedExpectedAppId,
            callback.NormalizedCallbackUrl,
            output,
            ct);

        var evidence = ResolveEvidence(callback, subscribedApps);
        output.WriteLine($"EVIDENCE = {evidence}");
        return 0;
    }

    private static async Task<CallbackInspectionResult> InspectCallbackAsync(
        int idBase,
        IWhatsAppWabaRoutingProvider routingProvider,
        HttpClient httpClient,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine("=== CALLBACK SELF-CHECK ===");
        WhatsAppWabaRoutingConfiguration routing;
        try
        {
            routing = await routingProvider.GetAsync(idBase, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine("CALLBACK_ROUTING_RESOLVED = False");
            output.WriteLine("CALLBACK_HOST = N/A");
            output.WriteLine("CALLBACK_HTTP = N/A");
            output.WriteLine("CALLBACK_REACHABLE = False");
            output.WriteLine("CALLBACK_ELAPSED_MS = 0");
            output.WriteLine($"CALLBACK_ERROR_TYPE = {ex.GetType().Name}");
            output.WriteLine($"CALLBACK_ERROR_SUMMARY = {Sanitize(ex.Message)}");
            return new CallbackInspectionResult(RoutingResolved: false, Reachable: false, NormalizeCallbackUri(null));
        }

        var callbackHost = TryGetHost(routing.CallbackUrl);
        output.WriteLine("CALLBACK_ROUTING_RESOLVED = True");
        output.WriteLine($"CALLBACK_HOST = {callbackHost}");

        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var verifyUrl = BuildCallbackVerificationUrl(routing.CallbackUrl, routing.VerifyToken, challenge);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.GetAsync(verifyUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();
            var reachable = response.IsSuccessStatusCode && string.Equals(body.Trim(), challenge, StringComparison.Ordinal);
            output.WriteLine($"CALLBACK_HTTP = {(int)response.StatusCode}");
            output.WriteLine($"CALLBACK_REACHABLE = {reachable}");
            output.WriteLine($"CALLBACK_ELAPSED_MS = {stopwatch.ElapsedMilliseconds}");
            output.WriteLine($"CALLBACK_ERROR_TYPE = {(reachable ? "N/A" : "CallbackVerificationFailed")}");
            output.WriteLine($"CALLBACK_ERROR_SUMMARY = {(reachable ? "N/A" : "El callback no devolvio el challenge esperado.")}");
            return new CallbackInspectionResult(RoutingResolved: true, reachable, NormalizeCallbackUri(routing.CallbackUrl));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            output.WriteLine("CALLBACK_HTTP = N/A");
            output.WriteLine("CALLBACK_REACHABLE = False");
            output.WriteLine($"CALLBACK_ELAPSED_MS = {stopwatch.ElapsedMilliseconds}");
            output.WriteLine($"CALLBACK_ERROR_TYPE = {ResolveCallbackExceptionType(ex)}");
            output.WriteLine($"CALLBACK_ERROR_SUMMARY = {Sanitize(ex.Message)}");
            return new CallbackInspectionResult(RoutingResolved: true, Reachable: false, NormalizeCallbackUri(routing.CallbackUrl));
        }
    }

    private static async Task<SubscribedAppsInspectionResult> InspectSubscribedAppsAsync(
        HttpClient httpClient,
        string graphBaseUrl,
        WhatsAppRuntimeCredential credential,
        string wabaId,
        string expectedAppId,
        string expectedCallback,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine("=== SUBSCRIBED_APPS GET ===");
        output.WriteLine($"EXPECTED_APP_ID = {ValueOrEmpty(expectedAppId)}");

        var baseUrl = (string.IsNullOrWhiteSpace(graphBaseUrl) ? "https://graph.facebook.com" : graphBaseUrl).TrimEnd('/');
        var version = (string.IsNullOrWhiteSpace(credential.GraphVersion) ? "v26.0" : credential.GraphVersion).Trim('/');
        var uri = $"{baseUrl}/{version}/{Uri.EscapeDataString(wabaId)}/subscribed_apps?fields={Uri.EscapeDataString("id,override_callback_uri")}&limit=100";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        output.WriteLine($"SUBSCRIBED_APPS_HTTP = {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            var error = ParseGraphError(body);
            output.WriteLine("SUBSCRIBED_APPS_COUNT = N/A");
            output.WriteLine("EXPECTED_APP_ID_FOUND = False");
            output.WriteLine("APP_ALREADY_SUBSCRIBED = False");
            output.WriteLine($"ERROR_CODE = {ValueOrEmpty(error.Code)}");
            output.WriteLine($"ERROR_TYPE = {ValueOrEmpty(error.Type)}");
            output.WriteLine($"ERROR_SUMMARY = {ValueOrEmpty(error.Summary)}");
            return new SubscribedAppsInspectionResult(GraphSuccess: false, AppAlreadySubscribed: false, ExpectedAppIdFound: false);
        }

        var items = ParseSubscribedApps(body);
        var expectedFound = !string.IsNullOrWhiteSpace(expectedAppId)
            && items.Any(item => string.Equals(item.AppId, expectedAppId, StringComparison.Ordinal));
        var alreadySubscribed = IsOurSubscriptionConfirmed(items, expectedAppId, expectedCallback);

        output.WriteLine($"SUBSCRIBED_APPS_COUNT = {items.Count}");
        output.WriteLine($"EXPECTED_APP_ID_FOUND = {expectedFound}");
        output.WriteLine($"APP_ALREADY_SUBSCRIBED = {alreadySubscribed}");
        output.WriteLine("ERROR_CODE = N/A");
        output.WriteLine("ERROR_TYPE = N/A");
        output.WriteLine("ERROR_SUMMARY = N/A");
        return new SubscribedAppsInspectionResult(GraphSuccess: true, alreadySubscribed, expectedFound);
    }

    private static string ResolveEvidence(CallbackInspectionResult callback, SubscribedAppsInspectionResult subscribedApps)
    {
        if (!callback.RoutingResolved)
            return EvidenceRoutingConfigurationFailed;
        if (!callback.Reachable)
            return EvidenceCallbackSelfCheckFailed;
        if (!subscribedApps.GraphSuccess)
            return EvidenceGraphSubscribedAppsFailed;
        if (subscribedApps.AppAlreadySubscribed)
            return EvidenceAppAlreadySubscribed;
        if (!subscribedApps.ExpectedAppIdFound)
            return EvidenceAppNotSubscribed;
        return EvidenceNoFailureReproduced;
    }

    private static IReadOnlyList<SubscribedAppItem> ParseSubscribedApps(string body)
    {
        var items = new List<SubscribedAppItem>();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var item in data.EnumerateArray())
            {
                var appId = TryReadMetaId(item, "id");
                var callback = TryReadString(item, "override_callback_uri");
                if (appId is not null || !string.IsNullOrWhiteSpace(callback))
                    items.Add(new SubscribedAppItem(appId, callback));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return items;
    }

    private static GraphErrorInfo ParseGraphError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : string.Empty;
                var type = TryReadString(error, "type");
                var summary = Sanitize(TryReadString(error, "message"));
                return new GraphErrorInfo(code, type, summary);
            }
        }
        catch (JsonException)
        {
        }

        return new GraphErrorInfo(string.Empty, string.Empty, Sanitize(body));
    }

    private static bool IsOurSubscriptionConfirmed(IReadOnlyList<SubscribedAppItem> items, string expectedAppId, string expectedCallback)
    {
        foreach (var item in items)
        {
            if (item.AppId is not null)
            {
                if (string.Equals(item.AppId, expectedAppId, StringComparison.Ordinal)
                    && string.Equals(NormalizeCallbackUri(item.OverrideCallbackUri), expectedCallback, StringComparison.Ordinal))
                    return true;
                continue;
            }

            if (string.Equals(NormalizeCallbackUri(item.OverrideCallbackUri), expectedCallback, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string BuildCallbackVerificationUrl(string callbackUrl, string verifyToken, string challenge)
    {
        var separator = callbackUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{callbackUrl}{separator}hub.mode=subscribe&hub.verify_token={Uri.EscapeDataString(verifyToken)}&hub.challenge={Uri.EscapeDataString(challenge)}";
    }

    private static string NormalizeCallbackUri(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed.TrimEnd('/');
        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;
        return uri.GetLeftPart(UriPartial.Authority) + path + uri.Query;
    }

    private static string? TryReadMetaId(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
            JsonValueKind.Number when value.TryGetInt64(out var id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string TryReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string TryGetHost(string callbackUrl)
        => Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri) ? uri.Host : "N/A";

    private static string ResolveCallbackExceptionType(Exception ex)
        => ex is TaskCanceledException ? "Timeout" : ex.GetType().Name;

    private static string Sanitize(string? value)
    {
        var text = Regex.Replace(value ?? string.Empty, @"[\u0000-\u001F]+", " ").Trim();
        if (text.Length == 0)
            return "N/A";
        text = Regex.Replace(text, @"(?i)(hub\.verify_token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(access_token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(verify_token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(Authorization:\s*Bearer\s+)[A-Za-z0-9._\-]+", "$1[REDACTED]");
        return text.Length <= 300 ? text : text[..300] + "...";
    }

    private static string ValueOrEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? "N/A" : Sanitize(value);

    private static void WriteOwnershipBlocked(TextWriter output, string expectedAppId)
    {
        output.WriteLine("");
        output.WriteLine("=== CALLBACK SELF-CHECK ===");
        output.WriteLine("CALLBACK_ROUTING_RESOLVED = False");
        output.WriteLine("CALLBACK_HOST = N/A");
        output.WriteLine("CALLBACK_HTTP = N/A");
        output.WriteLine("CALLBACK_REACHABLE = False");
        output.WriteLine("CALLBACK_ELAPSED_MS = 0");
        output.WriteLine("CALLBACK_ERROR_TYPE = OWNERSHIP_BLOCKED");
        output.WriteLine("CALLBACK_ERROR_SUMMARY = Bloqueado antes de resolver routing.");
        output.WriteLine("");
        WriteSkippedSubscribedApps(output, expectedAppId, "OWNERSHIP_BLOCKED");
    }

    private static void WriteCredentialBlocked(TextWriter output, string expectedAppId)
    {
        output.WriteLine("");
        output.WriteLine("=== CALLBACK SELF-CHECK ===");
        output.WriteLine("CALLBACK_ROUTING_RESOLVED = False");
        output.WriteLine("CALLBACK_HOST = N/A");
        output.WriteLine("CALLBACK_HTTP = N/A");
        output.WriteLine("CALLBACK_REACHABLE = False");
        output.WriteLine("CALLBACK_ELAPSED_MS = 0");
        output.WriteLine("CALLBACK_ERROR_TYPE = CREDENTIAL_BLOCKED");
        output.WriteLine("CALLBACK_ERROR_SUMMARY = Bloqueado antes de llamar a Graph.");
        output.WriteLine("");
        WriteSkippedSubscribedApps(output, expectedAppId, "CREDENTIAL_BLOCKED");
    }

    private static void WriteSkippedSubscribedApps(TextWriter output, string expectedAppId, string evidence)
    {
        output.WriteLine("=== SUBSCRIBED_APPS GET ===");
        output.WriteLine($"EXPECTED_APP_ID = {ValueOrEmpty(expectedAppId)}");
        output.WriteLine("SUBSCRIBED_APPS_HTTP = N/A");
        output.WriteLine("SUBSCRIBED_APPS_COUNT = N/A");
        output.WriteLine("EXPECTED_APP_ID_FOUND = False");
        output.WriteLine("APP_ALREADY_SUBSCRIBED = False");
        output.WriteLine("ERROR_CODE = N/A");
        output.WriteLine("ERROR_TYPE = N/A");
        output.WriteLine("ERROR_SUMMARY = N/A");
        output.WriteLine($"EVIDENCE = {evidence}");
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return args[i + 1];
        }

        return null;
    }

    private sealed record CallbackInspectionResult(bool RoutingResolved, bool Reachable, string NormalizedCallbackUrl);
    private sealed record SubscribedAppsInspectionResult(bool GraphSuccess, bool AppAlreadySubscribed, bool ExpectedAppIdFound);
    private sealed record SubscribedAppItem(string? AppId, string OverrideCallbackUri);
    private sealed record GraphErrorInfo(string Code, string Type, string Summary);

    private sealed class ReadOnlyCentralBasesService(IConfiguration configuration) : ICentralBasesService
    {
        private const string SelectColumns = """
            id AS IdBase,
            idcliente AS IdCliente,
            ISNULL(nombre, '') AS Nombre,
            ISNULL(dbserver, '') AS DbServer,
            ISNULL(dbname, '') AS DbName,
            ISNULL(dbuser, '') AS DbUser,
            ISNULL(dbpassword, '') AS DbPassword,
            WebhookToken
            """;

        private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
            ?? throw new InvalidOperationException("No se configuro la cadena de conexion 'ConnectionStrings:AlfaCentral'.");

        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<BaseCentralDto>>(new NotSupportedException("Inspector read-only: GetByClienteAsync no se usa."));

        public async Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
        {
            var sql = $"""
                SELECT TOP (1) {SelectColumns}
                FROM dbo.bases
                WHERE id = @IdBase;
                """;
            await using var cn = new SqlConnection(ConnectionString);
            return await cn.QuerySingleOrDefaultAsync<BaseCentralDto>(new CommandDefinition(sql, new { IdBase = idBase }, cancellationToken: ct));
        }

        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<BaseCentralDto>>(new NotSupportedException("Inspector read-only: GetAllAsync no se usa."));

        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
            => Task.FromException<BaseCentralDto?>(new NotSupportedException("Inspector read-only: GetByWebhookTokenAsync no se usa."));

        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default)
            => Task.FromException<string>(new InvalidOperationException("La base central no tiene WebhookToken y el inspector read-only no puede generarlo."));
    }

    private sealed class OneShotConexionClienteService : IConexionClienteService
    {
        private SessionDto? _session;
        public event Action? SessionChanged;

        public string GetConnectionString()
            => _session is null
                ? string.Empty
                : new SqlConnectionStringBuilder
                {
                    DataSource = _session.Servidor,
                    InitialCatalog = _session.BaseDatos,
                    UserID = _session.Usuario,
                    Password = _session.Password,
                    TrustServerCertificate = _session.TrustServerCertificate,
                    ApplicationName = "AlfaCore"
                }.ConnectionString;

        public SessionDto? GetActiveSession() => _session;
        public void SetWebhookOverride(SessionDto session) { _session = session; SessionChanged?.Invoke(); }
        public void ClearWebhookOverride() { _session = null; SessionChanged?.Invoke(); }
        public IReadOnlyList<SessionDto> GetAllSessions() => _session is null ? [] : [_session];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => ClearWebhookOverride();
    }

    private sealed class NullAppEventService : IAppEventService
    {
        public Task<string> LogErrorAsync(string module, string action, Exception exception, string userMessage, object? data = null, AppEventSeverity severity = AppEventSeverity.Error, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<string> LogAuditAsync(string module, string action, string entityType, string entityId, string message, object? data = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, SqlConnection connection, SqlTransaction transaction, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<AuditActivityPageDto> GetActivityAsync(string entityType, string recordId, int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
            => Task.FromResult(new AuditActivityPageDto { PageNumber = pageNumber, PageSize = pageSize });

        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(CancellationToken ct = default)
            => Task.FromResult(new AuditSchemaAvailabilityDto { Available = false });

        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(SqlConnection connection, SqlTransaction? transaction = null, CancellationToken ct = default)
            => Task.FromResult(new AuditSchemaAvailabilityDto { Available = false });
    }

    private sealed class OneShotAppUserSessionService : IAppUserSessionService
    {
        public event Action? StateChanged { add { } remove { } }
        public bool IsAuthenticated => false;
        public AppUserSessionInfo? CurrentUser => null;
        public bool RequiresInternalLogin => false;
        public string? CurrentToken => null;
        public Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public void AdoptInternalUser(AppUserSessionInfo internalUser) => throw new NotSupportedException();
        public bool TryRestoreFromToken(string token) => false;
        public void Logout() { }
        public void HandleSqlSessionChanged() { }
        public string GetCurrentUserName(string fallback = "") => fallback;
        public bool IsAuthorizedForSession(Guid? activeSessionId) => true;
        public void EnsureAuthorizedForSession(Guid? activeSessionId) { }
    }

    private sealed class AllowAllConversacionesAuthorizationService : IConversacionesAuthorizationService
    {
        public Task<bool> CanManageAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanManageAsync(int? expectedBaseId, CancellationToken ct = default) => Task.FromResult(true);
        public Task EnsureCanManageAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanManageAsync(int? expectedBaseId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanAttendConversationAsync(long idConversacion, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanAttendConversationAsync(long idConversacion, string connectionString, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanUseWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanUseWhatsAppNumeroAsync(int idNumero, string connectionString, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
