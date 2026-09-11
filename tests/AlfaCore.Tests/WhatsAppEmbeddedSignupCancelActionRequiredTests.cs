using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// "Cancelar configuración": salida de UX para abandonar un onboarding atascado en ACTION_REQUIRED
/// (caso real: Base4264/Alfanet Papelera eligió BUSINESS_APP_COEXISTENCE por error). Cubre la
/// transición de dominio (CancelActionRequiredConfigurationAsync), sus guardas (misma Base, mismo
/// estado esperado), que nunca toca ownership/Vault, y que después de cancelar la Base puede iniciar
/// una configuración nueva -- incluso reutilizando el mismo WABA/phone ya ownership-reservado
/// (AlreadyOwnedByBase, no Conflict).
/// </summary>
public sealed class WhatsAppEmbeddedSignupCancelActionRequiredTests
{
    private const int Base4264 = 4264;
    private const int OtherBase = 84;

    private static WhatsAppEmbeddedSignupOptions BuildOptions() => new()
    {
        Enabled = true,
        AllowedBaseIds = [Base4264],
        AppId = "app-id",
        BusinessPortfolioId = "biz",
        SystemUserId = "sysuser",
        EmbeddedSignupConfigId = "cfg",
        GraphApiVersion = "v26.0",
        GraphBaseUrl = "https://graph.facebook.com",
        OnboardingExpirationMinutes = 30
    };

    private static WhatsAppEmbeddedOnboardingDto SeedItem(int idBase, WhatsAppEmbeddedOnboardingStatus status) => new()
    {
        IdOnboarding = Guid.NewGuid(),
        IdBase = idBase,
        IdCliente = "CLI",
        UsuarioIniciador = "eve",
        StateHash = new string('a', 64),
        OnboardingMode = WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence,
        Status = status,
        CurrentStep = status.ToString().ToUpperInvariant(),
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
        ModifiedAtUtc = DateTime.UtcNow.AddMinutes(-10),
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(20)
    };

    private static WhatsAppEmbeddedSignupOrchestrator BuildOrchestrator(
        CancelableStore store,
        IWhatsAppAssetOwnershipStore? ownershipStore = null,
        IWhatsAppCredentialVault? credentialVault = null)
        => new(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new ThrowingOAuthClient(),
            credentialVault ?? new ThrowingCredentialVault(),
            new ThrowingPhonePinVault(),
            new ThrowingManagementClient(),
            ownershipStore ?? new ThrowingOwnershipStore(),
            new NoopErrorLogger(),
            Options.Create(BuildOptions()));

    [Fact]
    public async Task ActionRequired_OwnBase_TransitionsToCancelled()
    {
        var store = new CancelableStore();
        var item = SeedItem(Base4264, WhatsAppEmbeddedOnboardingStatus.ActionRequired);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var cancelled = await orchestrator.CancelActionRequiredConfigurationAsync(item.IdOnboarding, Base4264);

        Assert.True(cancelled);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Cancelled, item.Status);
        Assert.Equal("CANCELLED", item.CurrentStep);
    }

    [Fact]
    public async Task ActionRequired_OtherBase_IsBlocked()
    {
        // El onboarding pertenece a Base4264; intentar cancelarlo pasando OtherBase (84) debe
        // rechazarse sin tocar la fila -- nunca cancela onboardings de otra Base.
        var store = new CancelableStore();
        var item = SeedItem(Base4264, WhatsAppEmbeddedOnboardingStatus.ActionRequired);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var cancelled = await orchestrator.CancelActionRequiredConfigurationAsync(item.IdOnboarding, OtherBase);

        Assert.False(cancelled);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.ActionRequired, item.Status);
    }

    [Fact]
    public async Task Ready_IsNotCancelableByThisAction()
    {
        var store = new CancelableStore();
        var item = SeedItem(Base4264, WhatsAppEmbeddedOnboardingStatus.Ready);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var cancelled = await orchestrator.CancelActionRequiredConfigurationAsync(item.IdOnboarding, Base4264);

        Assert.False(cancelled);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Ready, item.Status);
    }

    [Fact]
    public async Task Cancellation_NeverTouchesOwnershipStore()
    {
        // ThrowingOwnershipStore lanza NotSupportedException ante cualquier llamada: si la cancelación
        // completa sin excepción, es porque nunca invocó ownership.
        var store = new CancelableStore();
        var item = SeedItem(Base4264, WhatsAppEmbeddedOnboardingStatus.ActionRequired);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store, ownershipStore: new ThrowingOwnershipStore());

        var cancelled = await orchestrator.CancelActionRequiredConfigurationAsync(item.IdOnboarding, Base4264);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task Cancellation_NeverTouchesVault()
    {
        var store = new CancelableStore();
        var item = SeedItem(Base4264, WhatsAppEmbeddedOnboardingStatus.ActionRequired);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store, credentialVault: new ThrowingCredentialVault());

        var cancelled = await orchestrator.CancelActionRequiredConfigurationAsync(item.IdOnboarding, Base4264);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task AfterCancellation_CanStartEmbeddedSignup_ForTheSameBase()
    {
        var store = new CancelableStore();
        var item = SeedItem(Base4264, WhatsAppEmbeddedOnboardingStatus.ActionRequired);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        Assert.True(await orchestrator.CancelActionRequiredConfigurationAsync(item.IdOnboarding, Base4264));

        // Nuevo onboarding, ahora eligiendo "Quiero empezar con un WhatsApp nuevo" -> Standard.
        var result = await orchestrator.StartAsync(new WhatsAppEmbeddedStartRequest(
            Base4264, "ALFANET", "Eve", WhatsAppEmbeddedOnboardingMode.Standard));

        Assert.NotEqual(item.IdOnboarding, result.IdOnboarding);
        var created = await store.GetAsync(result.IdOnboarding);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Started, created!.Status);
        Assert.Equal(WhatsAppEmbeddedOnboardingMode.Standard, created.OnboardingMode);
    }

    [Fact]
    public async Task NewOnboardingSameBase_ReDiscoveringAlreadyOwnedWabaAndPhone_IsNotAConflict()
    {
        // Meta vuelve a devolver el mismo WABA/phone que ya tiene ownership de Base4264 (del intento
        // cancelado). ReserveWabaAsync/ReservePhoneAsync deben resolver AlreadyOwnedByBase, no
        // Conflict, y el pipeline debe seguir de largo sin ActionRequired ni exigir borrar nada.
        var item = new WhatsAppEmbeddedOnboardingDto
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = Base4264,
            IdCliente = "CLI4264",
            UsuarioIniciador = "eve",
            StateHash = new string('a', 64),
            OnboardingMode = WhatsAppEmbeddedOnboardingMode.Standard,
            Status = WhatsAppEmbeddedOnboardingStatus.Authorized,
            CurrentStep = "AUTHORIZED",
            TokenReference = "vault-ref-4264",
            StartedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };
        var store = new ProcessableStore(item);
        var meta = new AlreadyOwnedMetaClient();
        var ownership = new AlreadyOwnedOwnershipStore(Base4264);
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new ValidOAuthClient(),
            new ThrowingCredentialVault(),
            new ThrowingPhonePinVault(),
            meta,
            ownership,
            new NoopErrorLogger(),
            Options.Create(BuildOptions()));

        for (var i = 0; i < 6 && item.Status is not (WhatsAppEmbeddedOnboardingStatus.ActionRequired or WhatsAppEmbeddedOnboardingStatus.FailedFinal or WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess); i++)
            await orchestrator.ProcessNextStepAsync(item.IdOnboarding);

        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess, item.Status);
        Assert.Contains((Base4264, "2597305014055622"), ownership.WabaReservationAttempts);
        Assert.Contains((Base4264, "1362965780228889"), ownership.PhoneReservationAttempts);
    }

    // ---- doubles -----------------------------------------------------------------------

    private sealed class CancelableStore : IWhatsAppEmbeddedSignupStore
    {
        private readonly Dictionary<Guid, WhatsAppEmbeddedOnboardingDto> _items = [];
        public void Seed(WhatsAppEmbeddedOnboardingDto item) => _items[item.IdOnboarding] = item;

        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default)
        { _items[onboarding.IdOnboarding] = onboarding; return Task.CompletedTask; }

        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.TryGetValue(id, out var item) ? item : null);

        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(_items.Values.Where(x => x.IdBase == idBase).OrderByDescending(x => x.StartedAtUtc).FirstOrDefault());

        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string h, int b, string u, DateTime n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid id, WhatsAppEmbeddedOnboardingStatus e, WhatsAppEmbeddedOnboardingStatus n, string s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid id, string r, string m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid id, WhatsAppEmbeddedActionRequiredReason r, string s, string i, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid id, string c, string s, string i, DateTime n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid id, string c, string s, string i, string? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string w, DateTime n, DateTime e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid id, string w, DateTime? n, CancellationToken ct = default) => throw new NotSupportedException();

        // Mismo guard atómico que el store real (WhatsAppEmbeddedSignupStore.CancelActionRequiredAsync):
        // sólo transiciona si IdOnboarding + IdBase + Estado=ACTION_REQUIRED coinciden exactamente.
        public Task<bool> CancelActionRequiredAsync(Guid id, int idBase, CancellationToken ct = default)
        {
            if (!_items.TryGetValue(id, out var item) || item.IdBase != idBase || item.Status != WhatsAppEmbeddedOnboardingStatus.ActionRequired)
                return Task.FromResult(false);
            item.Status = WhatsAppEmbeddedOnboardingStatus.Cancelled;
            item.CurrentStep = "CANCELLED";
            return Task.FromResult(true);
        }
    }

    private sealed class ProcessableStore(WhatsAppEmbeddedOnboardingDto item) : IWhatsAppEmbeddedSignupStore
    {
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        public Task UpdateStatusAsync(Guid id, WhatsAppEmbeddedOnboardingStatus expected, WhatsAppEmbeddedOnboardingStatus next, string step, CancellationToken ct = default)
        { item.Status = next; item.CurrentStep = step; return Task.CompletedTask; }
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto o, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string h, int b, string u, DateTime n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid id, string r, string m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid id, WhatsAppEmbeddedActionRequiredReason r, string s, string i, CancellationToken ct = default)
        { item.Status = WhatsAppEmbeddedOnboardingStatus.ActionRequired; item.CurrentStep = "ACTION_REQUIRED"; return Task.CompletedTask; }
        public Task MarkRetryableFailureAsync(Guid id, string c, string s, string i, DateTime n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid id, string c, string s, string i, string? f = null, CancellationToken ct = default)
        { item.Status = WhatsAppEmbeddedOnboardingStatus.FailedFinal; item.CurrentStep = f ?? "FAILED"; return Task.CompletedTask; }
        public Task MarkReadyAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string w, DateTime n, DateTime e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid id, string w, DateTime? n, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class AlreadyOwnedOwnershipStore(int idBase) : IWhatsAppAssetOwnershipStore
    {
        public List<(int IdBase, string WabaId)> WabaReservationAttempts { get; } = [];
        public List<(int IdBase, string PhoneNumberId)> PhoneReservationAttempts { get; } = [];

        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int requestedIdBase, string metaBusinessId, CancellationToken ct = default)
        {
            WabaReservationAttempts.Add((requestedIdBase, wabaId));
            // Ya reservado por la MISMA base en el intento anterior (cancelado) -> AlreadyOwnedByBase, no Conflict.
            return Task.FromResult(new WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult.AlreadyOwnedByBase, idBase, wabaId));
        }

        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int requestedIdBase, CancellationToken ct = default)
        {
            PhoneReservationAttempts.Add((requestedIdBase, phoneNumberId));
            return Task.FromResult(new WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult.AlreadyOwnedByBase, idBase, phoneNumberId));
        }

        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Task.FromResult<WhatsAppPhoneOwnership?>(null);
    }

    private sealed class AlreadyOwnedMetaClient : IMetaWhatsAppManagementClient
    {
        private const string Waba = "2597305014055622";
        private const string Phone = "1362965780228889";

        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaAuthorizedBusiness>>([new("biz-4264", "AlfanetPapelera")]);
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaWabaAsset>>([new(Waba, businessId, "AlfanetPapelera")]);
        public Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference t, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaPhoneAsset>>([new(Phone, wabaId, "+1 555-365-8051", "AlfanetPapelera", "NOT_APPLICABLE", "UNKNOWN", MetaPhoneRegistrationStatus.RegistrationRequired)]);
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaMessageTemplate>>([]);
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult(MetaPhoneRegistrationStatus.RegistrationRequired);
        public Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pin, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference t, CancellationToken ct = default)
            => Task.FromResult(MetaCustomerPaymentReadiness.Unknown);
    }

    private sealed class ThrowingOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string w, int i, string m, CancellationToken ct = default) => throw new NotSupportedException("No debería invocarse al cancelar.");
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string p, string w, int i, CancellationToken ct = default) => throw new NotSupportedException("No debería invocarse al cancelar.");
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string w, CancellationToken ct = default) => throw new NotSupportedException("No debería invocarse al cancelar.");
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string p, CancellationToken ct = default) => throw new NotSupportedException("No debería invocarse al cancelar.");
    }

    private sealed class ThrowingCredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException("No debería invocarse al cancelar.");
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => throw new NotSupportedException("No debería invocarse al cancelar.");
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => throw new NotSupportedException("No debería invocarse al cancelar.");
    }

    private sealed class ThrowingPhonePinVault : IWhatsAppPhonePinVault
    {
        public Task<WhatsAppPhonePinReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppPhonePinReference r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppPhonePinReference r, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingManagementClient : IMetaWhatsAppManagementClient
    {
        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string b, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureSystemUserAssignmentAsync(string w, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureWabaSubscriptionAsync(string w, int i, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string w, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string w, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string p, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RegisterPhoneAsync(string p, WhatsAppPhonePinReference pin, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string w, WhatsAppCredentialReference t, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string c, WhatsAppVaultSecretContext v, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ValidOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string c, WhatsAppVaultSecretContext v, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference r, CancellationToken ct = default)
            => Task.FromResult(new MetaTokenInspectionResult(true, null, []));
    }

    private sealed class NoopErrorLogger : IWhatsAppEmbeddedSignupErrorLogger
    {
        public Task<string> LogAsync(Guid id, int b, string s, string c, string? w, string? p, int r, CancellationToken ct = default) => Task.FromResult("WAES-TEST-STUB");
    }
}
