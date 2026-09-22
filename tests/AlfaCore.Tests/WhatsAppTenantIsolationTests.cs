using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
    public async Task EsBaseWithFootprint_UnknownPhone_IsRejectedBeforeAnyTenantWrite()
    {
        var guard = new WhatsAppWebhookTenantGuard(new OwnershipStore(null, hasFootprint: true), OptionsFor(84));
        var error = await Assert.ThrowsAsync<WhatsAppWebhookPhoneOwnershipMissingException>(() => guard.ValidateAsync(84, ["unknown-phone"]));
        Assert.Equal(84, error.CallbackBaseId);
        Assert.Equal("unknown-phone", error.PhoneNumberId);
    }

    [Fact]
    public async Task BaseWithoutEsFootprint_UnknownPhone_PassesThroughToLegacy()
        // Sin footprint ES, un phone sin ownership NO se rechaza: es un asset legacy.
        => await new WhatsAppWebhookTenantGuard(new OwnershipStore(null), OptionsFor(84)).ValidateAsync(84, ["unknown-phone"]);

    [Fact]
    public async Task EsBaseWithFootprint_MissingPhoneNumberId_IsRejectedBeforeAnyTenantWrite()
    {
        var error = await Assert.ThrowsAsync<WhatsAppWebhookPhoneNumberIdMissingException>(() =>
            new WhatsAppWebhookTenantGuard(new OwnershipStore(null, hasFootprint: true), OptionsFor(84)).ValidateAsync(84, []));
        Assert.Equal(84, error.CallbackBaseId);
    }

    [Fact]
    public async Task FeatureDisabled_GuardIsInert()
        => await new WhatsAppWebhookTenantGuard(new OwnershipStore(new("9201", "9101", 106, DateTime.UtcNow)),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = false })).ValidateAsync(84, ["9201"]);

    [Fact]
    public async Task OwnedPhone_IsAcceptedForMessagesAndStatuses()
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
    public async Task TemplateCredentialFromBaseBIsUnavailableToBaseA()
    {
        // El guard de envío de plantillas resuelve la credencial runtime por PhoneNumberId antes de
        // dejar salir un mensaje: si ese número quedó registrado con ownership de otra base (WABA de
        // la base B), la base A tiene que fallar cerrado (UnauthorizedAccessException) sin tocar el
        // vault -- nunca debe caer a legacy ni exponer la credencial de la base B.
        var vault = new CountingVault();
        var resolver = new WhatsAppRuntimeCredentialResolver(
            new OwnershipStore(new("9202", "9102", 2, DateTime.UtcNow)), vault, OptionsFor(1));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(1, 7, "9202", Legacy()));

        Assert.Equal(0, vault.Finds);
        Assert.Equal(0, vault.Reads);
    }

    [Fact]
    public async Task BaseWithNoOwnershipForPhone_ResolvesLegacyWithoutTouchingVault()
    {
        var store = new CountingOwnershipStore();   // GetPhoneOwnershipAsync -> null
        var vault = new CountingVault();
        var options = OptionsFor(84);

        await new WhatsAppWebhookTenantGuard(store, options).ValidateAsync(205, ["9201"]);

        var resolver = new WhatsAppRuntimeCredentialResolver(store, vault, options);
        var result = await resolver.ResolveAsync(205, 7, "9201", Legacy());

        // La decisión es por ownership: sin ownership => legacy. El vault nunca se toca.
        Assert.Equal(WhatsAppRuntimeCredentialOrigin.Legacy, result.Origin);
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
        var coexistenceSync = source.IndexOf("whatsAppCoexistenceSyncStore.Mark", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && messageParser > method && statusParser > messageParser && guard > statusParser && log > guard && status > log && conversation > log);
        // El tracking de sync de Coexistence (history/smb_app_state_sync) usa currentBaseId -- el
        // mismo valor que ya validó el guard -- y sólo se escribe después de él: cross-tenant queda
        // bloqueado en el mismo punto que el resto de la persistencia del webhook.
        Assert.True(coexistenceSync > guard);
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
        Assert.Contains("TryWriteWebhookFailureDiagnostic(correlationId, stage, ex)", programSource, StringComparison.Ordinal);
        Assert.Contains("Path.GetTempPath()", programSource, StringComparison.Ordinal);
        var middlewareSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "AppExceptionLoggingMiddleware.cs"));
        Assert.Contains("TryWriteWebhookFailureDiagnostic(context, ex)", middlewareSource, StringComparison.Ordinal);
        Assert.Contains("/api/conversaciones/whatsapp/webhook/[REDACTED]", middlewareSource, StringComparison.Ordinal);
        Assert.Contains("TraceStage = traceStage", programSource, StringComparison.Ordinal);
        Assert.Contains("PHONE_NUMBER_ID_FOUND", serviceSource, StringComparison.Ordinal);
        Assert.Contains("OWNERSHIP_RESOLVED", serviceSource, StringComparison.Ordinal);
        Assert.Contains("BEFORE_WEBHOOK_LOG", serviceSource, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_LOG_INSERTED", serviceSource, StringComparison.Ordinal);
    }

    // El 500 de este webhook casi nunca es una excepción -- HandleWhatsAppMessageAsync devuelve
    // Results.Problem(500) normalmente (p. ej. App Secret sin resolver, ver el comentario de
    // TenantizedWebhook_ResolvedBaseIdSelectsEsAppSecret_EvenWhenSessionHasNoActiveTenant más abajo),
    // así que ese camino no pasa por el catch/TryWriteWebhookFailureDiagnostic. El endpoint POST no es
    // invocable directamente en un test unitario (delegate de minimal API), así que esto verifica por
    // texto que el resultado devuelto por HandleWhatsAppMessageAsync se inspecciona y se registra antes
    // de devolverlo, sin alterarlo.
    [Fact]
    public void TenantizedWebhook_Returned5xxWithoutExceptionIsAlsoDiagnosed()
    {
        var programSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        var route = programSource.IndexOf("app.MapPost(\"/api/conversaciones/whatsapp/webhook/{token}\"", StringComparison.Ordinal);
        var outcomeCapture = programSource.IndexOf("var outcome = await HandleWhatsAppMessageAsync(", route, StringComparison.Ordinal);
        var outcomeCheck = programSource.IndexOf("if (outcome is IStatusCodeHttpResult { StatusCode: int returnedStatus } && returnedStatus >= 500)", outcomeCapture, StringComparison.Ordinal);
        var diagnosticCall = programSource.IndexOf("TryWriteWebhookOutcomeDiagnostic(correlationId, stage, returnedStatus, reasonCode)", outcomeCheck, StringComparison.Ordinal);
        var returnOutcome = programSource.IndexOf("return outcome;", diagnosticCall, StringComparison.Ordinal);

        Assert.True(route >= 0);
        Assert.True(outcomeCapture > route);
        Assert.True(outcomeCheck > outcomeCapture);
        Assert.True(diagnosticCall > outcomeCheck);
        Assert.True(returnOutcome > diagnosticCall);
        Assert.Contains("\"APP_SECRET_NOT_CONFIGURED\"", programSource, StringComparison.Ordinal);
        Assert.Contains("\"UNCLASSIFIED_5XX\"", programSource, StringComparison.Ordinal);

        var diagnosticMethod = programSource.IndexOf("private static void TryWriteWebhookOutcomeDiagnostic(", StringComparison.Ordinal);
        Assert.True(diagnosticMethod >= 0);
        var diagnosticDirectory = programSource.IndexOf("Path.Combine(Path.GetTempPath(), \"AlfaCore\", \"webhook-diagnostics\")", diagnosticMethod, StringComparison.Ordinal);
        Assert.True(diagnosticDirectory > diagnosticMethod);
    }

    // Complemento comportamental de la prueba estructural de arriba: HandleWhatsAppMessageAsync en sí
    // mismo (sin excepción) ya devuelve un Results.Problem(500) real cuando no hay App Secret resuelto
    // -- exactamente el resultado que el endpoint POST ahora inspecciona para diagnosticar.
    [Fact]
    public async Task HandleWhatsAppMessageAsync_ReturnsProblem500_WhenAppSecretIsNotConfigured()
    {
        const string phoneNumberId = "1233329726536711";
        var session = new SessionlessWebhookService();
        var config = CreateProxy<IConversacionesConfigService, WebhookConfigProxy>();
        ((WebhookConfigProxy)(object)config).Config = new ConversacionWhatsAppConfigDto(); // AppSecret legacy vacío
        var service = CreateProxy<IConversacionesService, WebhookServiceProxy>();
        var body = BuildWebhookPayload("messages", phoneNumberId);
        var requestContext = new DefaultHttpContext();
        requestContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

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
                AppSecret = string.Empty
            }),
            session,
            new OwnershipStore(null),
            CancellationToken.None,
            resolvedBaseId: 84);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal("WhatsApp App Secret no está configurado.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public void PlantillasRoute_UsesRouteBaseAsTenantAuthorityBeforeLoadingData()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesPlantillas.razor"));
        var routeBase = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "SaaSRoutePageBase.cs"));
        var route = source.IndexOf("@page \"/{idweb}/{idbase:int}/conversaciones/plantillas\"", StringComparison.Ordinal);
        var parameters = source.IndexOf("protected override async Task OnParametersSetAsync()", StringComparison.Ordinal);
        var protectedLoad = source.IndexOf("private async Task CompleteProtectedLoadAsync()", parameters, StringComparison.Ordinal);
        var waitSession = source.IndexOf("SessionInitialization.WaitUntilReadyAsync(_lifetimeCts.Token)", protectedLoad, StringComparison.Ordinal);
        var waitIdentity = source.IndexOf("TenantIdentityReadiness.WaitForTenantIdentityReadyAsync(_lifetimeCts.Token)", waitSession, StringComparison.Ordinal);
        var protectedRouteLoad = source.IndexOf("await LoadRouteTenantAsync();", waitIdentity, StringComparison.Ordinal);
        var loadRoute = source.IndexOf("private async Task LoadRouteTenantAsync()", StringComparison.Ordinal);
        var reset = source.IndexOf("ResetTenantData();", loadRoute, StringComparison.Ordinal);
        var ensure = source.IndexOf("EnsureRouteTenantReady(expectedBaseId)", loadRoute, StringComparison.Ordinal);
        var numeros = source.IndexOf("ConfigSvc.GetWhatsAppNumerosAsync(expectedBaseId)", loadRoute, StringComparison.Ordinal);
        var templates = source.IndexOf("ConversacionesSvc.GetTemplatesAsync(filters)", loadRoute, StringComparison.Ordinal);
        var detail = source.IndexOf("ConversacionesSvc.GetTemplateAsync(idPlantilla, expectedBaseId)", StringComparison.Ordinal);

        Assert.True(route >= 0);
        Assert.Contains("public int? idbase { get; set; }", routeBase, StringComparison.Ordinal);
        Assert.True(parameters > route && protectedLoad > parameters);
        Assert.True(waitSession > protectedLoad && waitIdentity > waitSession && protectedRouteLoad > waitIdentity);
        Assert.True(loadRoute > protectedRouteLoad);
        Assert.True(reset > loadRoute && ensure > reset && numeros > ensure && templates > numeros);
        Assert.True(detail > loadRoute);
        Assert.Contains("_loadError = \"No se pudo abrir la información de esta base.\";", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlantillasRoute_WaitsForSessionAndTenantIdentityBeforeFailClosedGuard()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesPlantillas.razor"));

        Assert.Contains("@inject AlfaCore.Services.IAppSessionInitialization SessionInitialization", source, StringComparison.Ordinal);
        Assert.Contains("@inject AlfaCore.Services.ITenantIdentityReadiness TenantIdentityReadiness", source, StringComparison.Ordinal);

        var protectedLoad = source.IndexOf("private async Task CompleteProtectedLoadAsync()", StringComparison.Ordinal);
        var sessionReady = source.IndexOf("await SessionInitialization.WaitUntilReadyAsync(_lifetimeCts.Token)", protectedLoad, StringComparison.Ordinal);
        var authenticated = source.IndexOf("if (!AppUserSession.IsAuthenticated)", sessionReady, StringComparison.Ordinal);
        var tenantReady = source.IndexOf("await TenantIdentityReadiness.WaitForTenantIdentityReadyAsync(_lifetimeCts.Token)", authenticated, StringComparison.Ordinal);
        var loadRoute = source.IndexOf("await LoadRouteTenantAsync();", tenantReady, StringComparison.Ordinal);
        var failClosedGuard = source.IndexOf("EnsureRouteTenantReady(expectedBaseId)", source.IndexOf("private async Task LoadRouteTenantAsync()", StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.True(protectedLoad >= 0);
        Assert.True(sessionReady > protectedLoad);
        Assert.True(authenticated > sessionReady);
        Assert.True(tenantReady > authenticated);
        Assert.True(loadRoute > tenantReady);
        Assert.True(failClosedGuard > loadRoute);
    }

    [Fact]
    public void PlantillasRoute_ReusesProtectedLoadForDirectInternalRefreshAndCurrentSessionCases()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesPlantillas.razor"));

        Assert.Contains("protected override async Task OnParametersSetAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await CompleteProtectedLoadAsync();", source, StringComparison.Ordinal);
        Assert.Contains("_ = InvokeAsync(CompleteProtectedLoadAsync);", source, StringComparison.Ordinal);
        Assert.Contains("active?.BaseId == expectedBaseId.Value", source, StringComparison.Ordinal);
        Assert.Contains("return AppUserSession.IsAuthorizedForSession(active.Id);", source, StringComparison.Ordinal);
        Assert.Contains("SessionSvc.SwitchSession(routeSession.Id);", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCts.Cancel();", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCts.Dispose();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlantillasServices_RejectExpectedBaseMismatchBeforeTemplateSql()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var config = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesConfigService.cs"));

        var getTemplates = service.IndexOf("public Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesAsync", StringComparison.Ordinal);
        var templatesGuard = service.IndexOf("ResolveTenantConnection(filters.ExpectedBaseId, \"GetTemplates\")", getTemplates, StringComparison.Ordinal);
        var templatesSql = service.IndexOf("FROM dbo.CONV_PLANTILLAS", getTemplates, StringComparison.Ordinal);
        var templatesConnection = service.IndexOf("new SqlConnection(tenant.ConnectionString)", getTemplates, StringComparison.Ordinal);

        var getTemplate = service.IndexOf("GetTemplateAsync(long idPlantilla, int? expectedBaseId", StringComparison.Ordinal);
        var detailGuard = service.IndexOf("ResolveTenantConnection(expectedBaseId, \"GetTemplate\")", getTemplate, StringComparison.Ordinal);
        var detailSql = service.IndexOf("WHERE IdPlantilla = @IdPlantilla", getTemplate, StringComparison.Ordinal);

        var getForConversation = service.IndexOf("GetTemplatesForConversationAsync(long idConversacion, int? expectedBaseId", StringComparison.Ordinal);
        var conversationGuard = service.IndexOf("ResolveTenantConnection(expectedBaseId, \"GetTemplatesForConversation\")", getForConversation, StringComparison.Ordinal);
        var fallback = service.IndexOf("var localTemplates = await GetTemplatesAsync(new ConversacionPlantillaFilters", getForConversation, StringComparison.Ordinal);
        var numberFilter = service.IndexOf("IdNumeroWhatsApp = conversation.IdNumeroWhatsApp", fallback, StringComparison.Ordinal);

        var numeros = config.IndexOf("GetWhatsAppNumerosAsync(int? expectedBaseId", StringComparison.Ordinal);
        var numerosGuard = config.IndexOf("ResolveTenantConnection(expectedBaseId, \"GetWhatsAppNumeros\")", numeros, StringComparison.Ordinal);
        var numerosSql = config.IndexOf("FROM dbo.CONV_WHATSAPP_NUMEROS", numeros, StringComparison.Ordinal);

        Assert.True(templatesGuard > getTemplates && templatesGuard < templatesSql && templatesConnection > templatesGuard);
        Assert.True(detailGuard > getTemplate && detailGuard < detailSql);
        Assert.True(conversationGuard > getForConversation && fallback > conversationGuard && numberFilter > fallback);
        Assert.True(numerosGuard > numeros && numerosGuard < numerosSql);
        Assert.Contains("active?.BaseId != expectedBaseId.Value", service, StringComparison.Ordinal);
        Assert.Contains("active?.BaseId != expectedBaseId.Value", config, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlantillasConfigService_WithExpectedBaseAAndActiveBaseB_FailsBeforeSql()
    {
        var session = new WebhookSessionService();
        session.SetWebhookOverride(new SessionDto
        {
            Id = Guid.NewGuid(),
            BaseId = 106,
            Nombre = "Base B",
            Servidor = "invalid-server",
            BaseDatos = "invalid-db",
            Usuario = "invalid-user",
            Password = "invalid-password",
            Activa = true
        });

        var service = new ConversacionesConfigService(
            new ConfigurationBuilder().Build(),
            session,
            CreateThrowingProxy<IAppEventService>(),
            Options.Create(new WhatsAppOptions()),
            new NullHttpClientFactory(),
            CreateThrowingProxy<IAppUserSessionService>(),
            CreateThrowingProxy<IConversacionesAuthorizationService>(),
            CreateThrowingProxy<ICentralBasesService>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetWhatsAppNumerosAsync(4264));
        Assert.Contains("no coincide", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlantillasPage_DiscardsLateAsyncResponsesAndClearsStaleTenantState()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesPlantillas.razor"));

        Assert.Contains("private int _loadGeneration;", source, StringComparison.Ordinal);
        Assert.Contains("var generation = ++_loadGeneration;", source, StringComparison.Ordinal);
        Assert.Contains("private bool IsCurrentRequest(int generation, int? expectedBaseId, int? selectedNumeroId)", source, StringComparison.Ordinal);
        Assert.Contains("if (!IsCurrentRequest(generation, expectedBaseId, selectedNumeroId))", source, StringComparison.Ordinal);
        Assert.Contains("_whatsappNumeros = [];", source, StringComparison.Ordinal);
        Assert.Contains("_allTemplates = [];", source, StringComparison.Ordinal);
        Assert.Contains("_templates = [];", source, StringComparison.Ordinal);
        Assert.Contains("_selectedId = 0;", source, StringComparison.Ordinal);
        Assert.Contains("protected override async Task OnParametersSetAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversacionesTemplatePaths_UseExpectedBaseAndTenantLoadGuard()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

        var programar = source.IndexOf("private async Task EnsureProgramarTemplatesAsync()", StringComparison.Ordinal);
        var programarCapture = source.IndexOf("TryCaptureTenantLoad(out var lease)", programar, StringComparison.Ordinal);
        var programarExpected = source.IndexOf("ExpectedBaseId = lease.BaseId", programar, StringComparison.Ordinal);
        var programarCurrent = source.IndexOf("IsTenantLoadCurrent(lease)", programar, StringComparison.Ordinal);

        var conversationTemplates = source.IndexOf("private async Task LoadTemplatesForConversationAsync()", StringComparison.Ordinal);
        var conversationCapture = source.IndexOf("TryCaptureTenantLoad(out var lease)", conversationTemplates, StringComparison.Ordinal);
        var conversationId = source.IndexOf("var conversationId = _selectedConversation.IdConversacion;", conversationCapture, StringComparison.Ordinal);
        var serviceCall = source.IndexOf("GetTemplatesForConversationAsync(conversationId, lease.BaseId)", conversationTemplates, StringComparison.Ordinal);
        var lateGuard = source.IndexOf("IsTemplateLoadCurrent(generation, conversationId, lease)", conversationTemplates, StringComparison.Ordinal);

        Assert.True(programar >= 0 && programarCapture > programar && programarExpected > programarCapture && programarCurrent > programarExpected);
        Assert.True(conversationTemplates >= 0 && conversationCapture > conversationTemplates && conversationId > conversationCapture && serviceCall > conversationId && lateGuard > serviceCall);
    }

    [Fact]
    public void Estadisticas_UsesExpectedBaseAcrossUiAndService()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesEstadisticas.razor"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var contract = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "IConversacionesService.cs"));
        var models = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Models", "ConversacionesModels.cs"));

        Assert.Contains("ConversationTenantLoadGuard", page, StringComparison.Ordinal);
        Assert.Contains("ConversacionesSvc.GetTechniciansAsync(lease.BaseId)", page, StringComparison.Ordinal);
        Assert.Contains("ExpectedBaseId = lease.BaseId", page, StringComparison.Ordinal);
        Assert.Contains("IsTenantLoadCurrent(lease)", page, StringComparison.Ordinal);
        Assert.Contains("Task<IReadOnlyList<ConversacionTecnicoOptionDto>> GetTechniciansAsync(int? expectedBaseId", contract, StringComparison.Ordinal);
        Assert.Contains("public int? ExpectedBaseId { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("GetConnectionStringForExpectedTenant(expectedBaseId, \"GetTechnicians\")", service, StringComparison.Ordinal);
        Assert.Contains("GetConnectionStringForExpectedTenant(filters.ExpectedBaseId, \"GetEstadisticas\")", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Informes_UsesRouteBaseForPagesServicesAndExcelExport()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesInformes.razor"));
        var detail = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesInformeDetalle.razor"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesInformesService.cs"));
        var contract = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "IConversacionesInformesService.cs"));
        var program = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));

        Assert.Contains("ConversationTenantLoadGuard", page, StringComparison.Ordinal);
        Assert.Contains("CurrentExpectedBaseIdOrNull()", page, StringComparison.Ordinal);
        Assert.Contains("InformesSvc.GetByPeriodoAsync(_anio, _mes, CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("InformesSvc.ListarAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("InformesSvc.GetAsync(id, CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("InformesSvc.GetDetalleAsync(IdDetalle, CurrentExpectedBaseIdOrNull())", detail, StringComparison.Ordinal);
        Assert.Contains("InformesSvc.GetTendenciaClienteAsync(IdDetalle, CurrentExpectedBaseIdOrNull())", detail, StringComparison.Ordinal);
        Assert.Contains("Task<ConversacionInformeMensualDto?> GetAsync(int idInforme, int? expectedBaseId", contract, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantConnection(expectedBaseId, \"GetInforme\")", service, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantConnection(expectedBaseId, \"GetDetalleInforme\")", service, StringComparison.Ordinal);
        Assert.Contains("var informe = await svc.GetAsync(idInforme, idbase, ct);", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracion_ValidatesRouteTenantBeforeProtectedLoads()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));
        var auth = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesAuthorizationService.cs"));
        var contract = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "IConversacionesAuthorizationService.cs"));

        var init = page.IndexOf("private async Task CompleteProtectedInitializationAsync()", StringComparison.Ordinal);
        var ensure = page.IndexOf("EnsureRouteTenantReady(expectedBaseId)", init, StringComparison.Ordinal);
        var canManage = page.IndexOf("AuthorizationSvc.CanManageAsync(expectedBaseId", init, StringComparison.Ordinal);
        var load = page.IndexOf("await LoadAsync();", init, StringComparison.Ordinal);

        Assert.True(init >= 0 && ensure > init && canManage > ensure && load > canManage);
        Assert.Contains("ConfigSvc.GetWhatsAppConfigAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetInstagramConfigAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetFacebookConfigAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetMercadoLibreConfigAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetAlfaKnowledgeConfigAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetAutomatizacionesConfigAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetClasificacionesAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetPrioridadConfigAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetUsuariosSistemaAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetConversacionAdministradoresAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetReglasAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetWhatsAppNumerosAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("ConvSvc.GetTechniciansAsync(CurrentExpectedBaseIdOrNull())", page, StringComparison.Ordinal);
        Assert.Contains("Task<bool> CanManageAsync(int? expectedBaseId", contract, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantConnection(expectedBaseId, \"CanManage\")", auth, StringComparison.Ordinal);
        Assert.Contains("active?.BaseId != expectedBaseId.Value", auth, StringComparison.Ordinal);
    }

    private static readonly WhatsAppEmbeddedSignupOptions EsAppSecretOptions = new()
    {
        Enabled = true,
        AllowAllTenants = true,
        AppSecret = "application-secret"
    };

    private static IReadOnlyCollection<WhatsAppPhoneOwnership> OwnedBy(int idBase)
        => [new WhatsAppPhoneOwnership("1233329726536711", "9101", idBase, DateTime.UtcNow)];

    [Fact]
    public void PhoneOwnedByResolvedBase_UsesApplicationSecret_NotLegacy()
    {
        var r = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(
            EsAppSecretOptions, resolvedBaseId: 84, OwnedBy(84),
            anyPhoneNumberIdInPayload: true, baseHasEmbeddedSignupFootprint: true, "legacy-tenant-secret");

        Assert.Equal(AlfaCore.Program.WhatsAppWebhookSecretOutcome.EmbeddedSignup, r.Outcome);
        Assert.Equal("application-secret", r.Secret);
    }

    [Fact]
    public void PhoneOwnedByAnotherBase_IsCrossTenantReject_NoSecret()
    {
        var r = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(
            EsAppSecretOptions, resolvedBaseId: 84, OwnedBy(106),
            anyPhoneNumberIdInPayload: true, baseHasEmbeddedSignupFootprint: true, "legacy-tenant-secret");

        Assert.Equal(AlfaCore.Program.WhatsAppWebhookSecretOutcome.RejectCrossTenant, r.Outcome);
        Assert.Equal(string.Empty, r.Secret);
    }

    [Fact]
    public void PhoneOwnedByResolvedBase_ButFeatureDisabled_RejectsEs_NeverLegacy()
    {
        var options = new WhatsAppEmbeddedSignupOptions { Enabled = false, AppSecret = "application-secret" };
        var r = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(
            options, resolvedBaseId: 84, OwnedBy(84),
            anyPhoneNumberIdInPayload: true, baseHasEmbeddedSignupFootprint: true, "legacy-tenant-secret");

        Assert.Equal(AlfaCore.Program.WhatsAppWebhookSecretOutcome.RejectEmbeddedSignupDisabled, r.Outcome);
        Assert.Equal(string.Empty, r.Secret);
    }

    [Fact]
    public void NoOwnership_BaseWithoutFootprint_UsesLegacyTenantSecret()
    {
        var r = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(
            EsAppSecretOptions, resolvedBaseId: 106, phoneOwnerships: [],
            anyPhoneNumberIdInPayload: true, baseHasEmbeddedSignupFootprint: false, "legacy-tenant-secret");

        Assert.Equal(AlfaCore.Program.WhatsAppWebhookSecretOutcome.Legacy, r.Outcome);
        Assert.Equal("legacy-tenant-secret", r.Secret);
    }

    [Fact]
    public void NoOwnership_BaseWithFootprint_IsUnknownPhoneReject_NoSecret()
    {
        var r = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(
            EsAppSecretOptions, resolvedBaseId: 84, phoneOwnerships: [],
            anyPhoneNumberIdInPayload: true, baseHasEmbeddedSignupFootprint: true, "legacy-tenant-secret");

        Assert.Equal(AlfaCore.Program.WhatsAppWebhookSecretOutcome.RejectUnknownPhoneForEsBase, r.Outcome);
        Assert.Equal(string.Empty, r.Secret);
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

        Assert.Equal(84, await AlfaCore.Program.TryResolveWebhookTenantAsync("test-route-token", bases, session, CancellationToken.None));

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
            new OwnershipStore(new("1233329726536711", "9101", 84, DateTime.UtcNow)),
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

    // Regresión del hotfix webhook-outcome-diag: el POST tenantizado de Meta llega sin sesión Blazor
    // (GetActiveSession() == null). Antes, HandleWhatsAppMessageAsync deducía la base con
    // GetActiveSession()?.BaseId ?? 0 => 0, ResolveWhatsAppWebhookAppSecret no matcheaba la allowlist
    // ES, caía al AppSecret legacy (vacío para Base84) y devolvía Results.Problem(500)
    // APP_SECRET_NOT_CONFIGURED (Stage=BODY_READ). El fix pasa el resolvedBaseId del token.
    [Fact]
    public async Task TenantizedWebhook_ResolvedBaseIdSelectsEsAppSecret_EvenWhenSessionHasNoActiveTenant()
    {
        const string esAppSecret = "es-application-secret";
        const string phoneNumberId = "1233329726536711";
        var session = new SessionlessWebhookService();
        var config = CreateProxy<IConversacionesConfigService, WebhookConfigProxy>();
        ((WebhookConfigProxy)(object)config).Config = new ConversacionWhatsAppConfigDto(); // AppSecret legacy vacío
        var service = CreateProxy<IConversacionesService, WebhookServiceProxy>();
        var serviceProxy = (WebhookServiceProxy)(object)service;
        var stages = new List<string>();
        var body = BuildWebhookPayload("messages", phoneNumberId);
        var requestContext = new DefaultHttpContext();
        requestContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        requestContext.Request.Headers["X-Hub-Signature-256"] = BuildSignature(esAppSecret, body);

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
                AppSecret = esAppSecret
            }),
            session,
            new OwnershipStore(new("1233329726536711", "9101", 84, DateTime.UtcNow)),
            CancellationToken.None,
            stages.Add,
            resolvedBaseId: 84);

        Assert.Null(session.GetActiveSession());
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Contains("SIGNATURE_VALID", stages);
        Assert.Contains("COMPLETED", stages);
        Assert.NotNull(serviceProxy.Request);
    }

    [Fact]
    public async Task TenantizedWebhook_WithResolvedBaseId_StillRejectsInvalidSignature()
    {
        const string esAppSecret = "es-application-secret";
        const string phoneNumberId = "1233329726536711";
        var session = new SessionlessWebhookService();
        var config = CreateProxy<IConversacionesConfigService, WebhookConfigProxy>();
        ((WebhookConfigProxy)(object)config).Config = new ConversacionWhatsAppConfigDto();
        var service = CreateProxy<IConversacionesService, WebhookServiceProxy>();
        var stages = new List<string>();
        var body = BuildWebhookPayload("messages", phoneNumberId);
        var requestContext = new DefaultHttpContext();
        requestContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        requestContext.Request.Headers["X-Hub-Signature-256"] = BuildSignature("firma-con-secret-equivocado", body);

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
                AppSecret = esAppSecret
            }),
            session,
            new OwnershipStore(new("1233329726536711", "9101", 84, DateTime.UtcNow)),
            CancellationToken.None,
            stages.Add,
            resolvedBaseId: 84);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.DoesNotContain("SIGNATURE_VALID", stages);
    }

    [Fact]
    public async Task TenantizedWebhook_PhoneOwnedByAnotherBase_IsRejectedCrossTenant()
    {
        const string esAppSecret = "es-application-secret";
        const string phoneNumberId = "1233329726536711";
        var session = new SessionlessWebhookService();
        var config = CreateProxy<IConversacionesConfigService, WebhookConfigProxy>();
        ((WebhookConfigProxy)(object)config).Config = new ConversacionWhatsAppConfigDto();
        var service = CreateProxy<IConversacionesService, WebhookServiceProxy>();
        var serviceProxy = (WebhookServiceProxy)(object)service;
        var stages = new List<string>();
        var body = BuildWebhookPayload("messages", phoneNumberId);
        var requestContext = new DefaultHttpContext();
        requestContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        requestContext.Request.Headers["X-Hub-Signature-256"] = BuildSignature(esAppSecret, body);

        // El token resuelve base 106, pero el phone_number_id es de la base 84 => cross-tenant.
        var result = await AlfaCore.Program.HandleWhatsAppMessageAsync(
            requestContext.Request,
            config,
            service,
            Options.Create(new WhatsAppEmbeddedSignupOptions
            {
                Enabled = true,
                AllowAllTenants = true,
                UseApplicationCentralConnection = true,
                AppSecret = esAppSecret
            }),
            session,
            new OwnershipStore(new("1233329726536711", "9101", 84, DateTime.UtcNow)),
            CancellationToken.None,
            stages.Add,
            resolvedBaseId: 106);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Contains("REJECTED_CROSS_TENANT", stages);
        Assert.DoesNotContain("SIGNATURE_VALID", stages);
        Assert.Null(serviceProxy.Request);
    }

    [Fact]
    public void AppSecretSelection_IsAllowlistIndependent_OwnershipDriven()
    {
        // Sin AllowAllTenants ni AllowedBaseIds: un asset con ownership ES para la base resuelta
        // igual usa el App Secret global (la lista sólo gatea iniciar onboardings, no el runtime).
        var options = new WhatsAppEmbeddedSignupOptions { Enabled = true, AppSecret = "application-secret" };

        var owned = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(
            options, resolvedBaseId: 142, OwnedBy(142),
            anyPhoneNumberIdInPayload: true, baseHasEmbeddedSignupFootprint: true, string.Empty);
        Assert.Equal(AlfaCore.Program.WhatsAppWebhookSecretOutcome.EmbeddedSignup, owned.Outcome);
        Assert.Equal("application-secret", owned.Secret);

        // Sin ownership y sin footprint => legacy (vacío aquí).
        var legacy = AlfaCore.Program.ResolveWhatsAppWebhookAppSecret(
            options, resolvedBaseId: 142, phoneOwnerships: [],
            anyPhoneNumberIdInPayload: true, baseHasEmbeddedSignupFootprint: false, string.Empty);
        Assert.Equal(AlfaCore.Program.WhatsAppWebhookSecretOutcome.Legacy, legacy.Outcome);
        Assert.Equal(string.Empty, legacy.Secret);
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

    private static TService CreateThrowingProxy<TService>()
        where TService : class
        => DispatchProxy.Create<TService, ThrowingProxy>();

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
        public SessionDto? GetWebhookOverride(int expectedBaseId) => session?.BaseId == expectedBaseId ? session : null;
        public void SetWebhookOverride(SessionDto value) { session = value; SessionChanged?.Invoke(); }
        public void ClearWebhookOverride() { session = null; SessionChanged?.Invoke(); }
        public IReadOnlyList<SessionDto> GetAllSessions() => session is null ? [] : [session];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string a, string b, string c, string d, string e) => throw new NotSupportedException();
        public void UpdateSession(Guid a, string b, string c, string d, string e, string f) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => session = null;
    }

    // Simula el POST servidor-servidor de Meta en IIS in-process: aunque TryResolveWebhookTenantAsync
    // llame a SetWebhookOverride, no hay sesión Blazor y GetActiveSession() devuelve null.
    private sealed class SessionlessWebhookService : ISessionService
    {
        private SessionDto? overrideValue;
        public event Action? SessionChanged;
        public bool WebhookOverrideWasSet { get; private set; }
        public string GetConnectionString() => "Server=test-server;Database=test-db;";
        public SessionDto? GetActiveSession() => null;
        public SessionDto? GetWebhookOverride(int expectedBaseId) => overrideValue?.BaseId == expectedBaseId ? overrideValue : null;
        public void SetWebhookOverride(SessionDto value) { overrideValue = value; WebhookOverrideWasSet = true; SessionChanged?.Invoke(); }
        public void ClearWebhookOverride() => SessionChanged?.Invoke();
        public IReadOnlyList<SessionDto> GetAllSessions() => [];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string a, string b, string c, string d, string e) => throw new NotSupportedException();
        public void UpdateSession(Guid a, string b, string c, string d, string e, string f) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() { }
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

    public class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class OwnershipStore(WhatsAppPhoneOwnership? phone, bool schemaAvailable = true, bool hasFootprint = false) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(schemaAvailable);
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult(phone);
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<bool> HasEmbeddedSignupFootprintAsync(int idBase, CancellationToken ct = default) => Task.FromResult(hasFootprint || phone is not null);
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string a, int b, string c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class MultiOwnershipStore(Dictionary<string, WhatsAppPhoneOwnership> phones) : IWhatsAppAssetOwnershipStore
    {
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult(phones.GetValueOrDefault(id));
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<bool> HasEmbeddedSignupFootprintAsync(int idBase, CancellationToken ct = default) => Task.FromResult(phones.Values.Any(p => p.IdBase == idBase));
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
