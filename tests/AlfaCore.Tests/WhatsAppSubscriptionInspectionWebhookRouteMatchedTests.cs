using System.Net;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;
using static AlfaCore.Configuration.WhatsAppSubscriptionInspectionCommand;

namespace AlfaCore.Tests;

/// <summary>
/// Regresión confirmada por git blame contra commit 6771726d ("Extend --inspect-whatsapp-subscription
/// with callback response diagnostics"): el diagnóstico WEBHOOK_ROUTE_MATCHED llamaba a
/// ICentralBasesService.GetByWebhookTokenAsync sin aislar la llamada. En el inspector real, esa
/// interfaz la implementa ReadOnlyCentralBasesService, cuyo GetByWebhookTokenAsync es un stub
/// deliberado que SIEMPRE lanza NotSupportedException ("no se usa" -- dejó de ser cierto cuando este
/// mismo diagnóstico empezó a llamarlo). Sin aislar esa llamada, la excepción escapaba al catch
/// general de InspectRoutingSourceAsync y descartaba TODO el resultado -- incluido
/// config.VerifyToken, que ya se había resuelto correctamente un par de líneas antes (confirmado
/// contra las tenant DB reales de Base4264/Base4271 en la investigación de esta misma sesión) --
/// produciendo el falso negativo VERIFY_TOKEN_PRESENT=False / VERIFY_TOKEN_SOURCE=NONE.
///
/// También cubre una segunda regresión encontrada en la misma investigación (Base4271, 2026-09):
/// RoutingSourceInspection.ToRoutingConfiguration() reconstruía el callback en paralelo a
/// WhatsAppWabaRoutingProvider real, y esa reconstrucción se olvidaba del WebhookPath
/// (PublicBaseUrl + "/" + WebhookToken, sin "/api/conversaciones/whatsapp/webhook" en medio) -- el
/// self-check del inspector terminaba probando una URL que EnsureWabaSubscriptionAsync jamás
/// construye. Se eliminó esa reconstrucción por completo: InspectCallbackAsync ahora siempre usa
/// routingProvider.GetAsync(idBase, ct) (el WhatsAppWabaRoutingProvider real), y
/// RoutingSourceInspection quedó como sólo-presentación (TENANT_PUBLIC_BASE_URL_*,
/// GLOBAL_CALLBACK_BASE_URL_*, VERIFY_TOKEN_SOURCE, etc.) -- ToRoutingConfiguration() se borró en
/// vez de dejarlo como lógica paralela muerta.
/// </summary>
public sealed class WhatsAppSubscriptionInspectionWebhookRouteMatchedTests
{
    private const int IdBase = 4271;
    private const string Phone = "1373763429148369";
    private const string Waba = "1760901255041127";
    private const string ExpectedAppId = "1436083307772786";
    private const string AccessToken = "runtime-access-token-never-printed";

    [Fact]
    public async Task WebhookRouteMatchedLookupThrowsNotSupportedException_DoesNotDiscardAlreadyResolvedVerifyTokenOrWebhookToken()
    {
        var centralBases = new ThrowingWebhookLookupCentralBasesService(new BaseCentralDto
        {
            IdBase = IdBase,
            IdCliente = "ALFANET",
            Nombre = "Base4271",
            DbServer = "tenant-server",
            DbName = "tenant-db",
            DbUser = "tenant-user",
            DbPassword = "tenant-password",
            WebhookToken = "webhook-token-present-non-empty"
        });
        var session = new NoopSessionService();
        var configService = new FixedWhatsAppConfigConversacionesConfigService(new ConversacionWhatsAppConfigDto
        {
            VerifyToken = "resolved-verify-token-non-empty",
            PublicBaseUrl = string.Empty
        });
        var embeddedOptions = new WhatsAppEmbeddedSignupOptions { CallbackBaseUrl = "https://alfacentral.ddns.net/" };

        var result = await InspectRoutingSourceAsync(IdBase, centralBases, session, configService, embeddedOptions, CancellationToken.None);

        Assert.True(result.VerifyTokenPresent);
        Assert.True(result.WebhookTokenPresent);
        Assert.Null(result.WebhookRouteMatched);
        Assert.DoesNotContain("NotSupportedException", result.RoutingFailureReason);
        Assert.DoesNotContain("resolved-verify-token-non-empty", result.RoutingFailureReason);
        Assert.DoesNotContain("webhook-token-present-non-empty", result.RoutingFailureReason);
    }

    [Fact]
    public async Task WebhookRouteMatchedLookupSucceeds_StillReportsTrueOrFalseNormally()
    {
        var centralBase = new BaseCentralDto
        {
            IdBase = IdBase,
            IdCliente = "ALFANET",
            Nombre = "Base4271",
            DbServer = "tenant-server",
            DbName = "tenant-db",
            DbUser = "tenant-user",
            DbPassword = "tenant-password",
            WebhookToken = "webhook-token-present-non-empty"
        };
        var centralBases = new SucceedingWebhookLookupCentralBasesService(centralBase, matches: true);
        var session = new NoopSessionService();
        var configService = new FixedWhatsAppConfigConversacionesConfigService(new ConversacionWhatsAppConfigDto
        {
            VerifyToken = "resolved-verify-token-non-empty",
            PublicBaseUrl = string.Empty
        });
        var embeddedOptions = new WhatsAppEmbeddedSignupOptions { CallbackBaseUrl = "https://alfacentral.ddns.net/" };

        var result = await InspectRoutingSourceAsync(IdBase, centralBases, session, configService, embeddedOptions, CancellationToken.None);

        Assert.True(result.WebhookRouteMatched);
    }

    [Fact]
    public async Task WebhookTokenAbsent_NeverAttemptsTheLookup_ReportsNAWithoutCallingCentralBases()
    {
        var centralBase = new BaseCentralDto
        {
            IdBase = IdBase,
            IdCliente = "ALFANET",
            Nombre = "Base4271",
            DbServer = "tenant-server",
            DbName = "tenant-db",
            DbUser = "tenant-user",
            DbPassword = "tenant-password",
            WebhookToken = null
        };
        var centralBases = new ThrowingWebhookLookupCentralBasesService(centralBase);
        var session = new NoopSessionService();
        var configService = new FixedWhatsAppConfigConversacionesConfigService(new ConversacionWhatsAppConfigDto
        {
            VerifyToken = "resolved-verify-token-non-empty",
            PublicBaseUrl = string.Empty
        });
        var embeddedOptions = new WhatsAppEmbeddedSignupOptions { CallbackBaseUrl = "https://alfacentral.ddns.net/" };

        var result = await InspectRoutingSourceAsync(IdBase, centralBases, session, configService, embeddedOptions, CancellationToken.None);

        Assert.False(result.WebhookTokenPresent);
        Assert.Null(result.WebhookRouteMatched);
        Assert.True(result.VerifyTokenPresent);
    }

    /// <summary>
    /// Caso obligatorio del pedido: PublicBaseUrl + WebhookPath default + WebhookToken sintético a
    /// través del WhatsAppWabaRoutingProvider REAL (no una reimplementación) -- el self-check debe
    /// terminar pidiendo exactamente .../api/conversaciones/whatsapp/webhook/{token}. El
    /// inspectRoutingSource que se le pasa a ExecuteAsync devuelve a propósito una URL/tokens
    /// DISTINTOS ("should-not-be-used...") -- si el callback terminara reflejando esos valores en vez
    /// de los del routing provider real, probaría que sigue existiendo una reconstrucción paralela.
    /// </summary>
    [Fact]
    public async Task InspectorSelfCheck_UsesRealRoutingProvider_DefaultWebhookPath_MatchesEnsureWabaSubscriptionAsyncShape()
    {
        var routingProvider = BuildRealRoutingProvider(
            publicBaseUrl: "https://alfacentral.ddns.net/",
            webhookPath: "/api/conversaciones/whatsapp/webhook",
            webhookToken: "synthetic-safe-token-default",
            verifyToken: "synthetic-verify-token-default");
        var misleadingSource = MisleadingRoutingSource();

        var output = await RunSelfCheckAsync(routingProvider, misleadingSource);

        Assert.Contains("CALLBACK_HOST = alfacentral.ddns.net", output);
        Assert.Contains("CALLBACK_PATH = /api/conversaciones/whatsapp/webhook/{token}", output);
        Assert.Contains("CALLBACK_REACHABLE = True", output);
        AssertNoSecretsOrMisleadingValuesLeaked(output, "synthetic-safe-token-default", "synthetic-verify-token-default");
    }

    /// <summary>
    /// Segundo caso obligatorio del pedido: un WebhookPath custom por tenant (CONV_WHATSAPP_WEBHOOK_PATH
    /// distinto al default) debe reflejarse tal cual -- confirma que el fix no hardcodea
    /// "/api/conversaciones/whatsapp/webhook", sino que reusa GetWebhookUrl()/el routing provider
    /// real, que sí respeta el override de tenant.
    /// </summary>
    [Fact]
    public async Task InspectorSelfCheck_UsesRealRoutingProvider_CustomTenantWebhookPath_DoesNotHardcodeDefault()
    {
        var routingProvider = BuildRealRoutingProvider(
            publicBaseUrl: "https://alfacentral.ddns.net/",
            webhookPath: "/custom/tenant/webhook/path",
            webhookToken: "synthetic-safe-token-custom",
            verifyToken: "synthetic-verify-token-custom");
        var misleadingSource = MisleadingRoutingSource();

        var output = await RunSelfCheckAsync(routingProvider, misleadingSource);

        Assert.Contains("CALLBACK_PATH = /custom/tenant/webhook/path/{token}", output);
        Assert.DoesNotContain("CALLBACK_PATH = /api/conversaciones/whatsapp/webhook/{token}", output);
        Assert.Contains("CALLBACK_REACHABLE = True", output);
        AssertNoSecretsOrMisleadingValuesLeaked(output, "synthetic-safe-token-custom", "synthetic-verify-token-custom");
    }

    private static IWhatsAppWabaRoutingProvider BuildRealRoutingProvider(string publicBaseUrl, string webhookPath, string webhookToken, string verifyToken)
    {
        var centralBase = new BaseCentralDto
        {
            IdBase = IdBase,
            IdCliente = "ALFANET",
            Nombre = "Base4271",
            DbServer = "tenant-server",
            DbName = "tenant-db",
            DbUser = "tenant-user",
            DbPassword = "tenant-password",
            WebhookToken = webhookToken
        };
        var centralBases = new SucceedingWebhookLookupCentralBasesService(centralBase, matches: true);
        var session = new NoopSessionService();
        var configService = new FixedWhatsAppConfigConversacionesConfigService(new ConversacionWhatsAppConfigDto
        {
            PublicBaseUrl = publicBaseUrl,
            WebhookPath = webhookPath,
            VerifyToken = verifyToken
        });
        var embeddedOptions = Options.Create(new WhatsAppEmbeddedSignupOptions());
        // El WhatsAppWabaRoutingProvider REAL -- el mismo tipo que usa EnsureWabaSubscriptionAsync en
        // producción, no una reimplementación de test.
        return new WhatsAppWabaRoutingProvider(centralBases, session, configService, embeddedOptions);
    }

    private static Func<int, CancellationToken, Task<RoutingSourceInspection>> MisleadingRoutingSource()
        => (_, _) => Task.FromResult(BuildRoutingSourceInspection(
            IdBase, string.Empty, "https://should-not-be-used.example.com",
            "should-not-be-used-verify-token", "should-not-be-used-webhook-token"));

    private static async Task<string> RunSelfCheckAsync(IWhatsAppWabaRoutingProvider routingProvider, Func<int, CancellationToken, Task<RoutingSourceInspection>> inspectRoutingSource)
    {
        using var handler = new EchoChallengeHandler();
        using var client = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await ExecuteAsync(
            IdBase,
            Phone,
            Waba,
            ExpectedAppId,
            new NoopOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, IdBase, DateTime.UtcNow)),
            new NoopCredentialResolver(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", AccessToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup)),
            routingProvider,
            client,
            "https://graph.facebook.com",
            output,
            CancellationToken.None,
            inspectRoutingSource);

        Assert.Equal(0, exitCode);
        return output.ToString();
    }

    /// <summary>
    /// Nunca se imprime WebhookToken/VerifyToken reales. La cadena "should-not-be-used" del
    /// inspectRoutingSource SÍ puede aparecer -- eso es la sección "=== ROUTING SOURCE ===", que
    /// sigue siendo sólo-presentación por diseño (RoutingSourceInspection no participa del callback,
    /// pero sus TENANT_PUBLIC_BASE_URL_*/GLOBAL_CALLBACK_BASE_URL_* siguen mostrando lo que devuelve
    /// inspectRoutingSource). Lo que prueba la desacoplación es que CALLBACK_HOST/CALLBACK_PATH
    /// (aserciones aparte, en el test) reflejen el routing provider real, no esta fuente.
    /// </summary>
    private static void AssertNoSecretsOrMisleadingValuesLeaked(string output, string webhookToken, string verifyToken)
    {
        Assert.DoesNotContain(webhookToken, output);
        Assert.DoesNotContain(verifyToken, output);
    }

    /// <summary>Responde el self-check GET con el hub.challenge recibido -- sin esto CALLBACK_REACHABLE
    /// daría False y no probaría nada sobre el shape del path (que ya se imprime antes del GET).</summary>
    private sealed class EchoChallengeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var challenge = string.Empty;
            foreach (var part in query)
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 2 && pieces[0] == "hub.challenge")
                    challenge = Uri.UnescapeDataString(pieces[1]);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(challenge) });
        }
    }

    private sealed class NoopOwnershipStore(WhatsAppPhoneOwnership? ownership) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Task.FromResult(ownership);
    }

    private sealed class NoopCredentialResolver(WhatsAppRuntimeCredential credential) : IWhatsAppRuntimeCredentialResolver
    {
        public Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default)
            => Task.FromResult(credential);
    }

    private sealed class NoopSessionService : ISessionService
    {
        public event Action? SessionChanged;
        public string GetConnectionString() => string.Empty;
        public SessionDto? GetActiveSession() => null;
        public void SetWebhookOverride(SessionDto session) { }
        public void ClearWebhookOverride() { }
        public IReadOnlyList<SessionDto> GetAllSessions() => [];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => throw new NotSupportedException();
    }

    /// <summary>Reproduce exactamente el stub real de ReadOnlyCentralBasesService.GetByWebhookTokenAsync
    /// (siempre lanza NotSupportedException) -- es la causa raíz confirmada, no una simplificación.</summary>
    private sealed class ThrowingWebhookLookupCentralBasesService(BaseCentralDto centralBase) : ICentralBasesService
    {
        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default) => Task.FromResult<BaseCentralDto?>(centralBase);
        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
            => Task.FromException<BaseCentralDto?>(new NotSupportedException("Inspector read-only: GetByWebhookTokenAsync no se usa."));
        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class SucceedingWebhookLookupCentralBasesService(BaseCentralDto centralBase, bool matches) : ICentralBasesService
    {
        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default) => Task.FromResult<BaseCentralDto?>(centralBase);
        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(matches ? centralBase : null);
        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedWhatsAppConfigConversacionesConfigService(ConversacionWhatsAppConfigDto config) : IConversacionesConfigService
    {
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default) => Task.FromResult(config);
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(int? expectedBaseId, CancellationToken ct = default) => Task.FromResult(config);
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> GenerateWhatsAppWebPairingAsync(ConversacionWhatsAppWebPairingRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> ClearWhatsAppWebPairingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreTokensAsync(ConversacionMercadoLibreConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigForConnectionAsync(string connectionString, ConversacionAlfaKnowledgeConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConnectionTestResultDto> TestAlfaKnowledgeConnectionAsync(ConversacionAlfaKnowledgeConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAutomatizacionesConfigAsync(ConversacionAutomatizacionesConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SavePrioridadConfigAsync(ConversacionPrioridadConfigDto c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroByInstanceNameAsync(string instanceName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto> UpsertEmbeddedSignupWhatsAppNumeroForBaseAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppNumeroWebSessionAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetPortfolioNameAsync(int idBase, string metaBusinessId, string portfolioName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryReserveResolutionAttemptAsync(int idBase, string metaBusinessId, TimeSpan throttleWindow, CancellationToken ct = default) => throw new NotSupportedException();
        public Task BackfillNumeroMetaIdentityAsync(int idNumero, string metaBusinessId, string wabaId, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(IReadOnlyCollection<string> wabaIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(IReadOnlyCollection<string> wabaIds, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryReserveWabaResolutionAttemptAsync(int idBase, string wabaId, TimeSpan throttleWindow, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetWabaOwningBusinessIdAsync(int idBase, string wabaId, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveConversacionAdministradoresAsync(IReadOnlyList<string> usuarios, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionesInboxPreferenceDto> GetInboxPreferenceAsync(string userName, string? sistema, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveInboxPreferenceAsync(string userName, string? sistema, ConversacionesInboxPreferenceDto preference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveReglaAsync(ConversacionReglaDto regla, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteReglaAsync(int idRegla, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
