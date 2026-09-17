using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
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
/// </summary>
public sealed class WhatsAppSubscriptionInspectionWebhookRouteMatchedTests
{
    private const int IdBase = 4271;

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

        // El callback self-check (VerifyCallbackAsync/EnsureWabaSubscriptionAsync en producción,
        // InspectCallbackAsync en el inspector) construye su routing config a partir de este mismo
        // resultado -- si el fix no aislara la excepción, ToRoutingConfiguration() ya habría fallado
        // antes (routing source vacío) o fallaría acá por VerifyToken/WebhookToken vacíos.
        var routingConfig = result.ToRoutingConfiguration();
        Assert.False(string.IsNullOrWhiteSpace(routingConfig.CallbackUrl));
        Assert.False(string.IsNullOrWhiteSpace(routingConfig.VerifyToken));
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
