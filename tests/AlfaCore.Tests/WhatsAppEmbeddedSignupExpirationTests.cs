using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Transición de dominio automática STARTED → EXPIRED (WhatsAppEmbeddedSignupOrchestrator.ReconcileExpiredAsync
/// + IWhatsAppEmbeddedSignupStore.ExpireStaleStartedAsync). Distinta de HandleCancellationAsync: no requiere
/// state/usuario/StateHash. Es la red de seguridad automática para popups abandonados, sesión Blazor perdida,
/// servidor reiniciado o callback nunca recibido — se evalúa de forma transparente en GetStatusAsync,
/// GetLatestStatusForBaseAsync y StartAsync (antes del guard "ya hay configuración en curso").
/// </summary>
public sealed class WhatsAppEmbeddedSignupExpirationTests
{
    [Fact]
    public async Task StartedNotExpired_DoesNotExpire()
    {
        var store = new ExpiringOnboardingStore();
        var item = Seeded(106, WhatsAppEmbeddedOnboardingStatus.Started, expired: false, consumed: false);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var status = await orchestrator.GetStatusAsync(item.IdOnboarding);

        Assert.NotNull(status);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Started, status!.Status);
        Assert.Equal(0, store.EffectiveExpireCount);
    }

    [Fact]
    public async Task StartedExpired_StateNotConsumed_TransitionsToExpired()
    {
        var store = new ExpiringOnboardingStore();
        var item = Seeded(106, WhatsAppEmbeddedOnboardingStatus.Started, expired: true, consumed: false);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var status = await orchestrator.GetStatusAsync(item.IdOnboarding);

        Assert.NotNull(status);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Expired, status!.Status);
        Assert.Equal("La autorización no se completó a tiempo.", status.Message);
        Assert.Equal(1, store.EffectiveExpireCount);
    }

    [Fact]
    public async Task StartedExpired_StateAlreadyConsumed_DoesNotExpire()
    {
        var store = new ExpiringOnboardingStore();
        var item = Seeded(106, WhatsAppEmbeddedOnboardingStatus.Started, expired: true, consumed: true);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var status = await orchestrator.GetStatusAsync(item.IdOnboarding);

        Assert.NotNull(status);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Started, status!.Status);
        Assert.Equal(0, store.EffectiveExpireCount);
    }

    [Fact]
    public async Task FailedFinal_IsNeverTouchedByExpiration()
    {
        var store = new ExpiringOnboardingStore();
        var item = Seeded(106, WhatsAppEmbeddedOnboardingStatus.FailedFinal, expired: true, consumed: false);
        item.ErrorSummary = "No se pudo completar la configuración con Meta.";
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var status = await orchestrator.GetLatestStatusForBaseAsync(106);

        Assert.NotNull(status);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.FailedFinal, status!.Status);
        Assert.Equal(0, store.EffectiveExpireCount);
    }

    [Fact]
    public async Task Ready_IsNeverTouchedByExpiration()
    {
        var store = new ExpiringOnboardingStore();
        var item = Seeded(106, WhatsAppEmbeddedOnboardingStatus.Ready, expired: true, consumed: false);
        store.Seed(item);
        var orchestrator = BuildOrchestrator(store);

        var status = await orchestrator.GetLatestStatusForBaseAsync(106);

        Assert.NotNull(status);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Ready, status!.Status);
        Assert.Equal(0, store.EffectiveExpireCount);
    }

    [Fact]
    public async Task ExpiringOneBase_DoesNotAffectAnotherBase()
    {
        var store = new ExpiringOnboardingStore();
        var target = Seeded(4264, WhatsAppEmbeddedOnboardingStatus.Started, expired: true, consumed: false);
        var other = Seeded(84, WhatsAppEmbeddedOnboardingStatus.Started, expired: true, consumed: false);
        store.Seed(target);
        store.Seed(other);
        var orchestrator = BuildOrchestrator(store, allowedBaseIds: [4264, 84]);

        await orchestrator.GetStatusAsync(target.IdOnboarding);

        var untouched = await store.GetAsync(other.IdOnboarding);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Started, untouched!.Status);
        Assert.Equal(1, store.EffectiveExpireCount);
    }

    [Fact]
    public async Task StartAsync_AfterAutoExpiringStaleStarted_CanCreateNewOnboarding()
    {
        var store = new ExpiringOnboardingStore();
        var stale = Seeded(106, WhatsAppEmbeddedOnboardingStatus.Started, expired: true, consumed: false);
        store.Seed(stale);
        var orchestrator = BuildOrchestrator(store);

        var result = await orchestrator.StartAsync(new WhatsAppEmbeddedStartRequest(106, "ALFANET", "Eve", WhatsAppEmbeddedOnboardingMode.Standard));

        Assert.NotEqual(stale.IdOnboarding, result.IdOnboarding);
        var previouslyStale = await store.GetAsync(stale.IdOnboarding);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Expired, previouslyStale!.Status);
        var created = await store.GetAsync(result.IdOnboarding);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Started, created!.Status);
    }

    [Fact]
    public async Task ConcurrentExpireAttempts_OnlyOneTransitionIsEffective()
    {
        var store = new ExpiringOnboardingStore();
        var item = Seeded(106, WhatsAppEmbeddedOnboardingStatus.Started, expired: true, consumed: false);
        store.Seed(item);

        var first = await store.ExpireStaleStartedAsync(item.IdOnboarding, 106);
        var second = await store.ExpireStaleStartedAsync(item.IdOnboarding, 106);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(2, store.ExpireCallCount);
        Assert.Equal(1, store.EffectiveExpireCount);
    }

    private static WhatsAppEmbeddedOnboardingDto Seeded(int idBase, WhatsAppEmbeddedOnboardingStatus status, bool expired, bool consumed)
    {
        var now = DateTime.UtcNow;
        return new WhatsAppEmbeddedOnboardingDto
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = idBase,
            IdCliente = "ALFANET",
            UsuarioIniciador = "Eve",
            StateHash = "hash",
            StateConsumedAtUtc = consumed ? now.AddMinutes(-1) : null,
            Status = status,
            CurrentStep = status.ToString().ToUpperInvariant(),
            StartedAtUtc = now.AddMinutes(-40),
            ExpiresAtUtc = expired ? now.AddMinutes(-10) : now.AddMinutes(20),
            ModifiedAtUtc = now.AddMinutes(-40)
        };
    }

    private static WhatsAppEmbeddedSignupOrchestrator BuildOrchestrator(ExpiringOnboardingStore store, int[]? allowedBaseIds = null)
        => new(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new NoopMetaOAuthClient(),
            new NoopCredentialVault(),
            Options.Create(new WhatsAppEmbeddedSignupOptions
            {
                Enabled = true,
                AllowedBaseIds = allowedBaseIds ?? [106, 4264, 84],
                AppId = "test-app-id",
                BusinessPortfolioId = "test-business-id",
                SystemUserId = "test-system-user-id",
                EmbeddedSignupConfigId = "test-config-id",
                GraphApiVersion = "v26.0",
                GraphBaseUrl = "https://graph.facebook.com",
                OnboardingExpirationMinutes = 30
            }));

    /// <summary>
    /// Doble en memoria que reproduce fielmente el UPDATE atómico guardado de
    /// WhatsAppEmbeddedSignupStore.ExpireStaleStartedAsync: sólo transiciona si TODAS las condiciones
    /// se cumplen, y sólo devuelve true en la llamada que efectivamente mutó la fila.
    /// </summary>
    private sealed class ExpiringOnboardingStore : IWhatsAppEmbeddedSignupStore
    {
        private readonly Dictionary<Guid, WhatsAppEmbeddedOnboardingDto> _items = [];
        public int ExpireCallCount { get; private set; }
        public int EffectiveExpireCount { get; private set; }

        public void Seed(WhatsAppEmbeddedOnboardingDto item) => _items[item.IdOnboarding] = item;

        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default)
        {
            _items[onboarding.IdOnboarding] = onboarding;
            return Task.CompletedTask;
        }

        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default)
            => Task.FromResult(_items.TryGetValue(idOnboarding, out var item) ? Clone(item) : null);

        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(_items.Values.Where(x => x.IdBase == idBase).OrderByDescending(x => x.StartedAtUtc).Select(Clone).FirstOrDefault());

        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExpireStaleStartedAsync(Guid idOnboarding, int idBase, CancellationToken ct = default)
        {
            ExpireCallCount++;
            if (!_items.TryGetValue(idOnboarding, out var item)
                || item.IdBase != idBase
                || item.Status != WhatsAppEmbeddedOnboardingStatus.Started
                || item.StateConsumedAtUtc is not null
                || item.ExpiresAtUtc > DateTime.UtcNow)
                return Task.FromResult(false);

            item.Status = WhatsAppEmbeddedOnboardingStatus.Expired;
            item.CurrentStep = "EXPIRED";
            item.ModifiedAtUtc = DateTime.UtcNow;
            EffectiveExpireCount++;
            return Task.FromResult(true);
        }

        private static WhatsAppEmbeddedOnboardingDto Clone(WhatsAppEmbeddedOnboardingDto item) => new()
        {
            IdOnboarding = item.IdOnboarding,
            IdBase = item.IdBase,
            IdCliente = item.IdCliente,
            UsuarioIniciador = item.UsuarioIniciador,
            CorrelationId = item.CorrelationId,
            StateHash = item.StateHash,
            StateConsumedAtUtc = item.StateConsumedAtUtc,
            Status = item.Status,
            OnboardingMode = item.OnboardingMode,
            CurrentStep = item.CurrentStep,
            MetaBusinessId = item.MetaBusinessId,
            StartedAtUtc = item.StartedAtUtc,
            ExpiresAtUtc = item.ExpiresAtUtc,
            ModifiedAtUtc = item.ModifiedAtUtc,
            RetryCount = item.RetryCount,
            NextAttemptUtc = item.NextAttemptUtc,
            ErrorCode = item.ErrorCode,
            ErrorSummary = item.ErrorSummary,
            IncidentId = item.IncidentId,
            TokenReference = item.TokenReference,
            ActionRequiredReason = item.ActionRequiredReason,
            ClaimedBy = item.ClaimedBy,
            ClaimExpiresAtUtc = item.ClaimExpiresAtUtc,
            RowVersion = item.RowVersion
        };
    }

    private sealed class NoopMetaOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopCredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
