using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Flujo lógico completo de un cliente nuevo (Base 142) con AllowAllTenants=true: desde AUTHORIZED,
/// el worker (ProcessNextStepAsync) descubre WABA/phone, crea ownership para 142, asigna el system
/// user, suscribe la Meta App al WABA con el callback tenantizado de 142, y deja el onboarding listo
/// para el alta operativa — sin editar .env, sin entrar a Meta, sin comandos en el servidor.
/// Verifica además que Base84 / Base106 / bases legacy no se tocan.
/// </summary>
public sealed class EmbeddedSignupNewTenantFlowTests
{
    private const int NewBase = 142;

    private static WhatsAppEmbeddedSignupOptions BuildOptions(bool webhookRouting) => new()
    {
        Enabled = true,
        AllowAllTenants = true,
        AllowedBaseIds = [],
        WorkerEnabled = true,
        WebhookRoutingEnabled = webhookRouting,
        AppId = "app-id",
        BusinessPortfolioId = "biz",
        SystemUserId = "sysuser",
        EmbeddedSignupConfigId = "cfg",
        GraphApiVersion = "v26.0",
        GraphBaseUrl = "https://graph.facebook.com",
        UseApplicationCentralConnection = true,
        AppSecret = "app-secret",
        DataProtectionKeysPath = @"C:\AlfaCore\Keys",
        CallbackBaseUrl = "https://alfacentral.example",
        OnboardingExpirationMinutes = 30,
        MaxRetryCount = 8
    };

    private static (WhatsAppEmbeddedSignupOrchestrator Orchestrator, WhatsAppEmbeddedOnboardingDto Item, SpyManagement Meta, SpyOwnership Ownership)
        Build(bool webhookRouting)
    {
        var item = new WhatsAppEmbeddedOnboardingDto
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = NewBase,
            IdCliente = "CLI142",
            UsuarioIniciador = "eve142",
            StateHash = new string('a', 64),
            OnboardingMode = WhatsAppEmbeddedOnboardingMode.Standard,
            Status = WhatsAppEmbeddedOnboardingStatus.Authorized,
            CurrentStep = "AUTHORIZED",
            TokenReference = "vault-ref-142",
            StartedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };
        var meta = new SpyManagement();
        var ownership = new SpyOwnership();
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            new SingleItemStore(item),
            new WhatsAppEmbeddedSignupStateProtector(),
            new ValidOAuth(),
            new NoopCredentialVault(),
            new NoopPhonePinVault(),
            meta,
            ownership,
            new NoopErrorLogger(),
            Options.Create(BuildOptions(webhookRouting)));
        return (orchestrator, item, meta, ownership);
    }

    private static async Task DriveAsync(WhatsAppEmbeddedSignupOrchestrator orchestrator, WhatsAppEmbeddedOnboardingDto item)
    {
        for (var i = 0; i < 12 && item.Status != WhatsAppEmbeddedOnboardingStatus.Importing; i++)
            await orchestrator.ProcessNextStepAsync(item.IdOnboarding);
    }

    [Fact]
    public async Task NewBase142_WithAllowAllTenants_ReachesOperationalUpsert_WithTenantSubscription()
    {
        var (orchestrator, item, meta, ownership) = Build(webhookRouting: true);

        await DriveAsync(orchestrator, item);

        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Importing, item.Status);
        Assert.Equal("READY_FOR_OPERATIONAL_UPSERT", item.CurrentStep);

        // ownership creado para 142 (y sólo para 142).
        Assert.Contains((NewBase, "waba-142"), ownership.WabaReservations);
        Assert.Contains((NewBase, "phone-142"), ownership.PhoneReservations);
        Assert.All(ownership.WabaReservations, r => Assert.Equal(NewBase, r.IdBase));
        Assert.All(ownership.PhoneReservations, r => Assert.Equal(NewBase, r.IdBase));

        // app suscripta al WABA con el callback tenantizado de la base 142.
        Assert.Contains((NewBase, "waba-142"), meta.SubscriptionCalls);
        Assert.All(meta.SubscriptionCalls, c => Assert.Equal(NewBase, c.IdBase));
        Assert.NotEmpty(meta.SystemUserAssignments);

        // Base84 / Base106 / legacy intactas: ningún reserve/subscribe con otro IdBase.
        Assert.DoesNotContain(ownership.WabaReservations, r => r.IdBase is 84 or 106);
        Assert.DoesNotContain(ownership.PhoneReservations, r => r.IdBase is 84 or 106);
        Assert.DoesNotContain(meta.SubscriptionCalls, c => c.IdBase is 84 or 106);
    }

    [Fact]
    public async Task NewBase142_WebhookRoutingDisabled_ReachesUpsert_ButWabaNotSubscribed()
    {
        var (orchestrator, item, meta, ownership) = Build(webhookRouting: false);

        await DriveAsync(orchestrator, item);

        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Importing, item.Status);
        Assert.Equal("READY_FOR_OPERATIONAL_UPSERT", item.CurrentStep);
        Assert.NotEmpty(ownership.WabaReservations);            // ownership igual se crea
        Assert.Empty(meta.SubscriptionCalls);                  // pero NO se suscribe la WABA => inbound no llega solo
    }

    // ---- doubles -----------------------------------------------------------------------

    private sealed class SpyManagement : IMetaWhatsAppManagementClient
    {
        public List<(int IdBase, string WabaId)> SubscriptionCalls { get; } = [];
        public List<string> SystemUserAssignments { get; } = [];
        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaAuthorizedBusiness>>([new("biz-142", "Cliente142")]);
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaWabaAsset>>([new("waba-142", businessId, "Cliente142")]);
        public Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default)
        { SystemUserAssignments.Add(wabaId); return Task.CompletedTask; }
        public Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference t, CancellationToken ct = default)
        { SubscriptionCalls.Add((idBase, wabaId)); return Task.CompletedTask; }
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaPhoneAsset>>([new("phone-142", wabaId, "+54 9 11 0000-0000", "Cliente142", "CONNECTED", "GREEN", MetaPhoneRegistrationStatus.Registered)]);
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaMessageTemplate>>([]);
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult(MetaPhoneRegistrationStatus.Registered);
        public Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pin, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult(MetaCustomerPaymentReadiness.Ready);
    }

    private sealed class SpyOwnership : IWhatsAppAssetOwnershipStore
    {
        public List<(int IdBase, string WabaId)> WabaReservations { get; } = [];
        public List<(int IdBase, string PhoneNumberId)> PhoneReservations { get; } = [];
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default)
        { WabaReservations.Add((idBase, wabaId)); return Task.FromResult(new WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult.Reserved, idBase, wabaId)); }
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default)
        { PhoneReservations.Add((idBase, phoneNumberId)); return Task.FromResult(new WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult.Reserved, idBase, phoneNumberId)); }
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Task.FromResult<WhatsAppPhoneOwnership?>(null);
    }

    private sealed class SingleItemStore(WhatsAppEmbeddedOnboardingDto item) : IWhatsAppEmbeddedSignupStore
    {
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        public Task UpdateStatusAsync(Guid id, WhatsAppEmbeddedOnboardingStatus expected, WhatsAppEmbeddedOnboardingStatus next, string step, CancellationToken ct = default)
        { Assert.Equal(item.Status, expected); item.Status = next; item.CurrentStep = step; return Task.CompletedTask; }
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto o, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string h, int b, string u, DateTime n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid id, string r, string m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid id, WhatsAppEmbeddedActionRequiredReason r, string s, string i, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid id, string c, string s, string i, DateTime n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid id, string c, string s, string i, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string w, DateTime n, DateTime e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid id, string w, DateTime? n, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ValidOAuth : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string c, WhatsAppVaultSecretContext v, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.FromResult(new MetaTokenInspectionResult(true, null, []));
    }

    private sealed class NoopCredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopPhonePinVault : IWhatsAppPhonePinVault
    {
        public Task<WhatsAppPhonePinReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppPhonePinReference r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppPhonePinReference r, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoopErrorLogger : IWhatsAppEmbeddedSignupErrorLogger
    {
        public Task<string> LogAsync(Guid id, int b, string s, string c, string? w, string? p, int r, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }
}
