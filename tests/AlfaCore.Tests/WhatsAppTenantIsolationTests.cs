using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppTenantIsolationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task MissingSchemaWithEmbeddedDisabled_PreservesLegacy()
    {
        var store = new OwnershipStore(null, false);
        await new WhatsAppWebhookTenantGuard(store, Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = false }))
            .ValidateAsync(84, ["1195619520311268"]);

        var resolver = new WhatsAppRuntimeCredentialResolver(store, new Vault(null, string.Empty),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = false }));
        var result = await resolver.ResolveAsync(84, null, "1195619520311268", Legacy());
        Assert.Equal(WhatsAppRuntimeCredentialOrigin.Legacy, result.Origin);
    }

    [Fact]
    public async Task MissingSchemaWithEmbeddedEnabled_FailsWithoutLegacyFallback()
    {
        var store = new OwnershipStore(null, false);
        var options = OptionsFor(84);
        await Assert.ThrowsAsync<WhatsAppEmbeddedSchemaUnavailableException>(() =>
            new WhatsAppWebhookTenantGuard(store, options).ValidateAsync(84, ["1195619520311268"]));
        await Assert.ThrowsAsync<WhatsAppEmbeddedSchemaUnavailableException>(() =>
            new WhatsAppRuntimeCredentialResolver(store, new Vault(null, string.Empty), options)
                .ResolveAsync(84, null, "1195619520311268", Legacy()));
    }

    [Fact]
    public async Task Base84TokenWithBase106Ownership_IsRejectedBeforeTenantWork()
    {
        var guard = new WhatsAppWebhookTenantGuard(new OwnershipStore(new("9201", "9101", 106, DateTime.UtcNow)), OptionsFor(84));
        var error = await Assert.ThrowsAsync<WhatsAppWebhookTenantMismatchException>(() => guard.ValidateAsync(84, ["9201"]));
        Assert.Equal(84, error.CallbackBaseId);
        Assert.Equal(106, error.OwnerBaseId);
    }

    [Fact]
    public async Task CallbackAndOwnershipSameBase_IsAccepted()
        => await new WhatsAppWebhookTenantGuard(new OwnershipStore(new("9201", "9101", 1, DateTime.UtcNow)), OptionsFor(1)).ValidateAsync(1, ["9201"]);

    [Fact]
    public async Task AllowedBaseWithUnknownPhone_IsRejectedBeforeAnyTenantWrite()
    {
        var guard = new WhatsAppWebhookTenantGuard(new OwnershipStore(null), OptionsFor(84));
        var error = await Assert.ThrowsAsync<WhatsAppWebhookPhoneOwnershipMissingException>(() => guard.ValidateAsync(84, ["unknown-phone"]));
        Assert.Equal(84, error.CallbackBaseId);
        Assert.Equal("unknown-phone", error.PhoneNumberId);
    }

    [Fact]
    public async Task AllowedBaseWithMissingPhoneNumberId_IsRejectedBeforeAnyTenantWrite()
    {
        var error = await Assert.ThrowsAsync<WhatsAppWebhookPhoneNumberIdMissingException>(() =>
            new WhatsAppWebhookTenantGuard(new OwnershipStore(null), OptionsFor(84)).ValidateAsync(84, []));
        Assert.Equal(84, error.CallbackBaseId);
    }

    [Fact]
    public async Task AllowedBaseWithOwnedPhone_IsAcceptedForMessagesAndStatuses()
    {
        var guard = new WhatsAppWebhookTenantGuard(new OwnershipStore(new("9201", "9101", 84, DateTime.UtcNow)), OptionsFor(84));
        await guard.ValidateAsync(84, ["9201"]);
    }

    [Fact]
    public async Task EmbeddedSignup_UsesVaultAndNeverLegacyToken()
    {
        var resolver = CreateResolver(new("9201", "9101", 1, DateTime.UtcNow), new("vault-ref"), "vault-token");
        var result = await resolver.ResolveAsync(1, 7, "9201", Legacy());
        Assert.Equal(WhatsAppRuntimeCredentialOrigin.EmbeddedSignup, result.Origin);
        Assert.Equal("vault-token", result.AccessToken);
        Assert.Equal("9101", result.WabaId);
    }

    [Fact]
    public async Task EmbeddedSignupWithoutVault_FailsWithoutLegacyFallback()
    {
        var resolver = CreateResolver(new("9201", "9101", 1, DateTime.UtcNow), null, "");
        var error = await Assert.ThrowsAsync<WhatsAppEmbeddedVaultUnavailableException>(() => resolver.ResolveAsync(1, 7, "9201", Legacy()));
        Assert.Contains("credencial segura", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddedSignupWithoutDataProtection_DoesNotReadVaultOrUseLegacyFallback()
    {
        var vault = new CountingVault();
        var options = Options.Create(new WhatsAppEmbeddedSignupOptions
        {
            Enabled = true,
            AllowedBaseIds = [84],
            GraphApiVersion = "v26.0"
        });
        var resolver = new WhatsAppRuntimeCredentialResolver(
            new OwnershipStore(new("9201", "9101", 84, DateTime.UtcNow)), vault, options);

        await Assert.ThrowsAsync<WhatsAppEmbeddedVaultUnavailableException>(() =>
            resolver.ResolveAsync(84, 7, "9201", Legacy()));

        Assert.Equal(0, vault.Finds);
        Assert.Equal(0, vault.Reads);
    }

    [Fact]
    public async Task BaseOutsideAllowedList_DoesNotConsultEmbeddedStores()
    {
        var store = new CountingOwnershipStore();
        var vault = new CountingVault();
        var options = OptionsFor(84);

        await new WhatsAppWebhookTenantGuard(store, options).ValidateAsync(205, ["9201"]);

        var resolver = new WhatsAppRuntimeCredentialResolver(store, vault, options);
        var result = await resolver.ResolveAsync(205, 7, "9201", Legacy());

        Assert.Equal(WhatsAppRuntimeCredentialOrigin.Legacy, result.Origin);
        Assert.Equal(0, store.SchemaChecks);
        Assert.Equal(0, store.PhoneLookups);
        Assert.Equal(0, vault.Finds);
        Assert.Equal(0, vault.Reads);
    }

    [Fact]
    public async Task TwoWabasInSameBase_ResolveCredentialByPhoneNumberId()
    {
        var store = new MultiOwnershipStore(new Dictionary<string, WhatsAppPhoneOwnership>
        {
            ["9201"] = new("9201", "9101", 1, DateTime.UtcNow),
            ["9202"] = new("9202", "9102", 1, DateTime.UtcNow)
        });
        var vault = new MultiVault();
        var resolver = new WhatsAppRuntimeCredentialResolver(store, vault, OptionsFor(1));
        Assert.Equal("token-9201", (await resolver.ResolveAsync(1, 1, "9201", Legacy())).AccessToken);
        Assert.Equal("token-9202", (await resolver.ResolveAsync(1, 2, "9202", Legacy())).AccessToken);
    }

    [Fact]
    public void WebhookGuardRunsBeforeAnyOperationalPersistenceAndAutomationUsesCommonSender()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var method = source.IndexOf("RegisterIncomingWebhookAsync", StringComparison.Ordinal);
        var guard = source.IndexOf("whatsAppWebhookTenantGuard.ValidateAsync", method, StringComparison.Ordinal);
        var log = source.IndexOf("var webhookLogId = await InsertWebhookLogAsync(", method, StringComparison.Ordinal);
        var status = source.IndexOf("UpdateWhatsAppMessageStatusAsync(status", method, StringComparison.Ordinal);
        var conversation = source.IndexOf("EnsureConversationAsync(incoming", method, StringComparison.Ordinal);
        var messageParser = source.IndexOf("var parsedMessages = ParseIncomingMessages", method, StringComparison.Ordinal);
        var statusParser = source.IndexOf("var parsedStatuses = ParseIncomingStatuses", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && messageParser > method && statusParser > messageParser && guard > statusParser && log > guard && status > log && conversation > log);
        Assert.Contains("SistemaAccion = \"BIENVENIDA\"", source, StringComparison.Ordinal);
        Assert.Contains("await SendMessageAsync(new ConversacionSendMessageRequest", source, StringComparison.Ordinal);
        Assert.Contains("GetTemplatesForConversationAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TextAndStatusWebhooks_DoNotResolveVaultCredentials()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var method = source.IndexOf("RegisterIncomingWebhookAsync", StringComparison.Ordinal);
        var withoutVault = source.IndexOf("var embeddedSignupWithoutVault", method, StringComparison.Ordinal);
        var attachments = source.IndexOf("if (incoming.Attachments.Count > 0 && whatsAppConfig is not null && !embeddedSignupWithoutVault)", method, StringComparison.Ordinal);
        var resolver = source.IndexOf("whatsAppRuntimeCredentialResolver.ResolveAsync", method, StringComparison.Ordinal);
        var status = source.IndexOf("UpdateWhatsAppMessageStatusAsync(status", method, StringComparison.Ordinal);

        Assert.True(method >= 0 && withoutVault > method && status > withoutVault && attachments > status && resolver > attachments);
    }

    [Fact]
    public void WebhookRuntimeWithoutWorker_DoesNotRequireDataProtection()
    {
        var options = WebhookOnlyStartupOptions();

        Assert.True(options.IsValidStartupConfiguration());
    }

    [Fact]
    public void WorkerWithoutDataProtection_IsRejectedAtStartup()
    {
        var options = WorkerStartupOptions();
        options.DataProtectionKeysPath = string.Empty;

        Assert.False(options.IsValidStartupConfiguration());
    }

    [Fact]
    public void WorkerWithoutGraphConfiguration_IsRejectedAtStartup()
    {
        var options = WebhookOnlyStartupOptions();
        options.WorkerEnabled = true;
        options.DataProtectionKeysPath = @"C:\AlfaCore\EmbeddedSignupKeys";

        Assert.False(options.IsValidStartupConfiguration());
    }

    [Fact]
    public void WebhookOnlyHost_RejectsOnboardingGraphOperationsExplicitly()
    {
        var options = WebhookOnlyStartupOptions();

        Assert.Throws<WhatsAppEmbeddedSignupOnboardingConfigurationException>(() => options.EnsureOnboardingGraphConfiguration());
    }

    [Fact]
    public void StartupValidation_UsesTheWorkerAwareEmbeddedSignupContract()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));

        Assert.Contains(".Validate(static options => options.IsValidStartupConfiguration(),", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantizedWhatsAppWebhookRoutes_DisableCachingBeforeResolvingTheRouteToken()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        var getRoute = source.IndexOf("app.MapGet(\"/api/conversaciones/whatsapp/webhook/{token}\"", StringComparison.Ordinal);
        var postRoute = source.IndexOf("app.MapPost(\"/api/conversaciones/whatsapp/webhook/{token}\"", StringComparison.Ordinal);
        var resolver = "TryResolveWebhookTenantAsync(token, basesService, sessionService, ct)";

        var getNoCache = source.IndexOf("DisableWebhookCaching(response);", getRoute, StringComparison.Ordinal);
        var getResolve = source.IndexOf(resolver, getRoute, StringComparison.Ordinal);
        var postNoCache = source.IndexOf("DisableWebhookCaching(response);", postRoute, StringComparison.Ordinal);
        var postResolve = source.IndexOf(resolver, postRoute, StringComparison.Ordinal);

        Assert.True(getRoute >= 0 && getNoCache > getRoute && getResolve > getNoCache);
        Assert.True(postRoute >= 0 && postNoCache > postRoute && postResolve > postNoCache);
        Assert.Contains("response.Headers.CacheControl = \"no-store, no-cache, max-age=0\";", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantizedWebhookRoute_ResolvesTheRawRouteTokenThroughCentralBases()
    {
        var programSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        var basesSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "CentralBasesService.cs"));
        var route = programSource.IndexOf("app.MapGet(\"/api/conversaciones/whatsapp/webhook/{token}\"", StringComparison.Ordinal);
        var resolution = programSource.IndexOf("TryResolveWebhookTenantAsync(token, basesService, sessionService, ct)", route, StringComparison.Ordinal);
        var lookup = programSource.IndexOf("basesService.GetByWebhookTokenAsync(token, ct)", StringComparison.Ordinal);
        var sessionOverride = programSource.IndexOf("sessionService.SetWebhookOverride", lookup, StringComparison.Ordinal);

        Assert.True(route >= 0 && resolution > route);
        Assert.True(lookup >= 0 && lookup < sessionOverride);
        Assert.Contains("WHERE WebhookToken = @WebhookToken", basesSource, StringComparison.Ordinal);
        Assert.Contains("new { WebhookToken = webhookToken.Trim() }", basesSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsAppWebhookRequiresAValidSignatureBeforePayloadProcessing()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        var handler = source.IndexOf("internal static async Task<IResult> HandleWhatsAppMessageAsync", StringComparison.Ordinal);
        var resolveSecret = source.IndexOf("ResolveWhatsAppWebhookAppSecret(", handler, StringComparison.Ordinal);
        var secretRequired = source.IndexOf("if (string.IsNullOrWhiteSpace(appSecret))", handler, StringComparison.Ordinal);
        var signatureCheck = source.IndexOf("if (!IsValidMetaSignature(rawPayload, appSecret, signature))", handler, StringComparison.Ordinal);
        var payloadParse = source.IndexOf("JsonDocument.Parse", handler, StringComparison.Ordinal);
        Assert.True(handler >= 0 && resolveSecret > handler && secretRequired > resolveSecret && signatureCheck > secretRequired && payloadParse > signatureCheck);
    }

    [Fact]
    public void TenantizedWebhook_FailureTraceCarriesOnlyCorrelationAndStageData()
    {
        var programSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        var serviceSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        Assert.Contains("var correlationId = Guid.NewGuid().ToString(\"N\")", programSource, StringComparison.Ordinal);
        Assert.Contains("WhatsApp tenant webhook trace {CorrelationId} {Stage}", programSource, StringComparison.Ordinal);
        Assert.Contains("SanitizeWebhookDiagnostic(ex.Message)", programSource, StringComparison.Ordinal);
        Assert.Contains("SanitizeWebhookDiagnostic(ex.StackTrace)", programSource, StringComparison.Ordinal);
        Assert.Contains("TraceStage = traceStage", programSource, StringComparison.Ordinal);
        Assert.Contains("PHONE_NUMBER_ID_FOUND", serviceSource, StringComparison.Ordinal);
        Assert.Contains("OWNERSHIP_RESOLVED", serviceSource, StringComparison.Ordinal);
        Assert.Contains("BEFORE_WEBHOOK_LOG", serviceSource, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_LOG_INSERTED", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedSignupBase_UsesApplicationSecretInsteadOfLegacyTenantSecret()
    {
        var options = new WhatsAppEmbeddedSignupOptions
        {
            Enabled = true,
            AllowedBaseIds = [84],
            AppSecret = "application-secret"
        };

        var secret = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(options, 84, "legacy-tenant-secret");

        Assert.Equal("application-secret", secret);
    }

    [Fact]
    public void BaseOutsideEmbeddedSignupAllowlist_PreservesLegacyWebhookSecret()
    {
        var options = new WhatsAppEmbeddedSignupOptions
        {
            Enabled = true,
            AllowedBaseIds = [84],
            AppSecret = "application-secret"
        };

        var secret = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(options, 106, "legacy-tenant-secret");

        Assert.Equal("legacy-tenant-secret", secret);
    }

    [Fact]
    public void MetaSignatureValidator_RejectsAnInvalidSignature()
    {
        const string payload = "{\"entry\":[]}";
        const string secret = "test-secret";
        var validHash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        var validSignature = "sha256=" + Convert.ToHexString(validHash).ToLowerInvariant();

        Assert.True(AlfaCore.Program.IsValidMetaSignature(payload, secret, validSignature));
        Assert.False(AlfaCore.Program.IsValidMetaSignature(payload, secret, "sha256=00"));
    }

    [Theory]
    [InlineData("messages")]
    [InlineData("statuses")]
    public async Task TenantizedWebhookReplay_ValidSignatureReachesThePersistenceBoundaryWithoutTenantWrites(string eventKind)
    {
        const string appSecret = "test-webhook-secret";
        const string phoneNumberId = "1233329726536711";
        var session = new WebhookSessionService();
        var bases = new WebhookCentralBasesService(new BaseCentralDto
        {
            IdBase = 84,
            Nombre = "ALFANET",
            DbServer = "test-server",
            DbName = "test-db",
            DbUser = "test-user"
        });
        var config = CreateProxy<IConversacionesConfigService, WebhookConfigProxy>();
        ((WebhookConfigProxy)(object)config).Config = new ConversacionWhatsAppConfigDto();
        var service = CreateProxy<IConversacionesService, WebhookServiceProxy>();
        var serviceProxy = (WebhookServiceProxy)(object)service;
        var stages = new List<string>();
        var body = BuildWebhookPayload(eventKind, phoneNumberId);
        var requestContext = new DefaultHttpContext();
        requestContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        requestContext.Request.Headers["X-Hub-Signature-256"] = BuildSignature(appSecret, body);

        Assert.True(await AlfaCore.Program.TryResolveWebhookTenantAsync("test-route-token", bases, session, CancellationToken.None));

        var result = await AlfaCore.Program.HandleWhatsAppMessageAsync(
            requestContext.Request,
            config,
            service,
            Options.Create(new WhatsAppEmbeddedSignupOptions
            {
                Enabled = true,
                AllowedBaseIds = [84],
                WorkerEnabled = false,
                WebhookRoutingEnabled = false,
                UseApplicationCentralConnection = true,
                AppSecret = appSecret
            }),
            session,
            CancellationToken.None,
            stages.Add);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(84, session.GetActiveSession()?.BaseId);
        Assert.NotNull(serviceProxy.Request);
        Assert.Contains("SIGNATURE_VALID", stages);
        Assert.Contains("JSON_PARSED", stages);
        Assert.Contains("COMPLETED", stages);
        Assert.Equal(0, serviceProxy.OperationalWrites);
    }

    private static WhatsAppRuntimeCredentialResolver CreateResolver(WhatsAppPhoneOwnership? owner, WhatsAppCredentialReference? reference, string secret)
        => new(new OwnershipStore(owner), new Vault(reference, secret), OptionsFor(1));
    private static IOptions<WhatsAppEmbeddedSignupOptions> OptionsFor(params int[] allowedBaseIds)
        => Options.Create(new WhatsAppEmbeddedSignupOptions
        {
            Enabled = true,
            AllowedBaseIds = allowedBaseIds,
            GraphApiVersion = "v26.0",
            DataProtectionKeysPath = @"C:\AlfaCore\EmbeddedSignupKeys"
        });
    private static WhatsAppEmbeddedSignupOptions WebhookOnlyStartupOptions()
        => new()
        {
            Enabled = true,
            AllowedBaseIds = [84],
            WorkerEnabled = false,
            WebhookRoutingEnabled = false,
            UseApplicationCentralConnection = true,
            AppSecret = "app-secret"
        };
    private static WhatsAppEmbeddedSignupOptions WorkerStartupOptions()
        => new()
        {
            Enabled = true,
            WorkerEnabled = true,
            AllowedBaseIds = [84],
            AppId = "app-id",
            BusinessPortfolioId = "business-id",
            SystemUserId = "system-user-id",
            EmbeddedSignupConfigId = "config-id",
            GraphApiVersion = "v26.0",
            GraphBaseUrl = "https://graph.facebook.com",
            UseApplicationCentralConnection = true,
            AppSecret = "app-secret",
            DataProtectionKeysPath = @"C:\AlfaCore\EmbeddedSignupKeys",
            OnboardingExpirationMinutes = 30,
            MaxRetryCount = 8
        };
    private static ConversacionWhatsAppConfigDto Legacy() => new() { AccessToken = "legacy-token", PhoneNumberId = "legacy", BusinessAccountId = "legacy-waba", ApiVersion = "v22.0" };

    private static TService CreateProxy<TService, TProxy>()
        where TService : class
        where TProxy : DispatchProxy
        => DispatchProxy.Create<TService, TProxy>();

    private static string BuildWebhookPayload(string eventKind, string phoneNumberId)
    {
        const string marker = "__PHONE_NUMBER_ID__";
        var payload = eventKind == "messages"
            ? """{"object":"whatsapp_business_account","entry":[{"changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"__PHONE_NUMBER_ID__"},"contacts":[{"wa_id":"5491100000000"}],"messages":[{"id":"wamid.synthetic-inbound","from":"5491100000000","timestamp":"1725900000","type":"text","text":{"body":"synthetic"}}]}}]}]}"""
            : """{"object":"whatsapp_business_account","entry":[{"changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"__PHONE_NUMBER_ID__"},"statuses":[{"id":"wamid.synthetic-status","status":"delivered","timestamp":"1725900000","recipient_id":"5491100000000"}]}}]}]}""";
        return payload.Replace(marker, phoneNumberId, StringComparison.Ordinal);
    }

    private static string BuildSignature(string appSecret, string body)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private sealed class WebhookSessionService : ISessionService
    {
        private SessionDto? session;
        public event Action? SessionChanged;
        public string GetConnectionString() => session is null ? string.Empty : "Server=test-server;Database=test-db;";
        public SessionDto? GetActiveSession() => session;
        public void SetWebhookOverride(SessionDto value) { session = value; SessionChanged?.Invoke(); }
        public void ClearWebhookOverride() { session = null; SessionChanged?.Invoke(); }
        public IReadOnlyList<SessionDto> GetAllSessions() => session is null ? [] : [session];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string a, string b, string c, string d, string e) => throw new NotSupportedException();
        public void UpdateSession(Guid a, string b, string c, string d, string e, string f) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => session = null;
    }

    private sealed class WebhookCentralBasesService(BaseCentralDto baseInfo) : ICentralBasesService
    {
        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string token, CancellationToken ct = default) => Task.FromResult<BaseCentralDto?>(baseInfo);
        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
    }

    public class WebhookConfigProxy : DispatchProxy
    {
        public ConversacionWhatsAppConfigDto Config { get; set; } = new();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == nameof(IConversacionesConfigService.GetWhatsAppConfigAsync)
                ? Task.FromResult(Config)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    public class WebhookServiceProxy : DispatchProxy
    {
        public ConversacionWebhookRequest? Request { get; private set; }
        public int OperationalWrites { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IConversacionesService.RegisterIncomingWebhookAsync))
                throw new NotSupportedException(targetMethod?.Name);

            Request = (ConversacionWebhookRequest?)args?[0];
            return Task.FromResult(new ConversacionWebhookResultDto());
        }
    }

    private sealed class OwnershipStore(WhatsAppPhoneOwnership? phone, bool schemaAvailable = true) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(schemaAvailable);
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult(phone);
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string a, int b, string c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class MultiOwnershipStore(Dictionary<string, WhatsAppPhoneOwnership> phones) : IWhatsAppAssetOwnershipStore
    {
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult(phones.GetValueOrDefault(id));
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string a, int b, string c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Vault(WhatsAppCredentialReference? reference, string secret) : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference?> FindActiveCredentialAsync(int a, string b, string c, CancellationToken ct = default) => Task.FromResult(reference);
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>(secret.AsMemory());
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class MultiVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference?> FindActiveCredentialAsync(int a, string b, string phone, CancellationToken ct = default) => Task.FromResult<WhatsAppCredentialReference?>(new($"ref-{phone}"));
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>(r.Value.Replace("ref-", "token-").AsMemory());
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class CountingOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        public int SchemaChecks { get; private set; }
        public int PhoneLookups { get; private set; }
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) { SchemaChecks++; return Task.FromResult(true); }
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) { PhoneLookups++; return Task.FromResult<WhatsAppPhoneOwnership?>(null); }
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string a, int b, string c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class CountingVault : IWhatsAppCredentialVault
    {
        public int Finds { get; private set; }
        public int Reads { get; private set; }
        public Task<WhatsAppCredentialReference?> FindActiveCredentialAsync(int a, string b, string c, CancellationToken ct = default) { Finds++; return Task.FromResult<WhatsAppCredentialReference?>(null); }
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) { Reads++; return Task.FromResult<ReadOnlyMemory<char>>(ReadOnlyMemory<char>.Empty); }
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
