using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppCoexistenceSyncTriggerTests
{
    [Fact]
    public async Task CoexistenceReady_RequestsContactsThenHistory_ForEachPhoneOnBizApp()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient();
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

        await trigger.TriggerInitialSyncsAsync(
            onboarding,
            [new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true)],
            new WhatsAppCredentialReference("token-ref"));

        Assert.Equal(
            [(WhatsAppCoexistenceSyncType.ContactState, "phone-1"), (WhatsAppCoexistenceSyncType.History, "phone-1")],
            management.Requests);
        var contacts = await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.ContactState);
        var history = await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.History);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Requested, contacts!.Status);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Requested, history!.Status);
        Assert.Equal("req-1", contacts.RequestId);
    }

    [Fact]
    public async Task StandardReady_NeverRequestsAnything()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient();
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.Standard);

        await trigger.TriggerInitialSyncsAsync(
            onboarding,
            [new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true)],
            new WhatsAppCredentialReference("token-ref"));

        Assert.Empty(management.Requests);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task PhoneNotOnBizApp_IsSkipped()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient();
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

        await trigger.TriggerInitialSyncsAsync(
            onboarding,
            [new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: false)],
            new WhatsAppCredentialReference("token-ref"));

        Assert.Empty(management.Requests);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task Reprocessing_DoesNotDuplicateRequests()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient();
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);
        var candidates = new[] { new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true) };

        await trigger.TriggerInitialSyncsAsync(onboarding, candidates, new WhatsAppCredentialReference("token-ref"));
        await trigger.TriggerInitialSyncsAsync(onboarding, candidates, new WhatsAppCredentialReference("token-ref"));
        await trigger.TriggerInitialSyncsAsync(onboarding, candidates, new WhatsAppCredentialReference("token-ref"));

        Assert.Equal(2, management.Requests.Count); // una vez contacts, una vez history -- nunca más.
    }

    [Fact]
    public async Task ContactsRequestFails_HistoryIsStillAttempted()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient { ThrowFor = WhatsAppCoexistenceSyncType.ContactState };
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

        await trigger.TriggerInitialSyncsAsync(
            onboarding,
            [new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true)],
            new WhatsAppCredentialReference("token-ref"));

        var contacts = await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.ContactState);
        var history = await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.History);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Failed, contacts!.Status);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Requested, history!.Status);
    }

    [Fact]
    public async Task HistoryRequestFails_DoesNotThrow()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient { ThrowFor = WhatsAppCoexistenceSyncType.History };
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

        var exception = await Record.ExceptionAsync(() => trigger.TriggerInitialSyncsAsync(
            onboarding,
            [new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true)],
            new WhatsAppCredentialReference("token-ref")));

        Assert.Null(exception);
        var history = await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.History);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Failed, history!.Status);
    }

    [Fact]
    public async Task WindowExpired_MarksExpiredWithoutCallingMeta()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient();
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, startedAtUtc: DateTime.UtcNow.AddHours(-25));

        await trigger.TriggerInitialSyncsAsync(
            onboarding,
            [new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true)],
            new WhatsAppCredentialReference("token-ref"));

        Assert.Empty(management.Requests);
        var contacts = await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.ContactState);
        var history = await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.History);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Expired, contacts!.Status);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Expired, history!.Status);
    }

    [Fact]
    public async Task MultiplePhoneNumberIds_TrackedIndependently()
    {
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient();
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboarding = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

        await trigger.TriggerInitialSyncsAsync(
            onboarding,
            [
                new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true),
                new WhatsAppCoexistencePhoneCandidate("phone-2", IsOnBizApp: true)
            ],
            new WhatsAppCredentialReference("token-ref"));

        Assert.Equal(4, management.Requests.Count);
        Assert.NotNull(await store.GetAsync(onboarding.IdBase, "phone-1", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.History));
        Assert.NotNull(await store.GetAsync(onboarding.IdBase, "phone-2", onboarding.IdOnboarding, WhatsAppCoexistenceSyncType.History));
    }

    [Fact]
    public async Task ReOnboard_SamePhone_AfterRealOffboard_RequestsHistoryAgain()
    {
        // Escenario del pedido de corrección: onboarding A completa su history sync one-shot para el
        // número X; después de un offboard real y un nuevo consentimiento, onboarding B (mismo IdBase,
        // mismo PhoneNumberId, IdOnboarding NUEVO) debe poder volver a pedir history -- el one-shot es
        // por intento de onboarding, no para siempre por número.
        var store = new MemorySyncStore();
        var management = new MemoryManagementClient();
        var trigger = new WhatsAppCoexistenceSyncTrigger(store, management, NullLogger<WhatsAppCoexistenceSyncTrigger>.Instance);
        var onboardingA = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);
        var candidates = new[] { new WhatsAppCoexistencePhoneCandidate("phone-1", IsOnBizApp: true) };

        await trigger.TriggerInitialSyncsAsync(onboardingA, candidates, new WhatsAppCredentialReference("token-ref"));
        Assert.Equal(2, management.Requests.Count); // contacts + history de A.

        // offboard real (fuera del alcance de este trigger) + nuevo Embedded Signup Coexistence => B.
        var onboardingB = CreateOnboarding(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);
        Assert.NotEqual(onboardingA.IdOnboarding, onboardingB.IdOnboarding);

        await trigger.TriggerInitialSyncsAsync(onboardingB, candidates, new WhatsAppCredentialReference("token-ref"));

        Assert.Equal(4, management.Requests.Count); // contacts + history de A, y de nuevo contacts + history de B.
        var rowA = await store.GetAsync(onboardingA.IdBase, "phone-1", onboardingA.IdOnboarding, WhatsAppCoexistenceSyncType.History);
        var rowB = await store.GetAsync(onboardingB.IdBase, "phone-1", onboardingB.IdOnboarding, WhatsAppCoexistenceSyncType.History);
        Assert.NotNull(rowA); // la fila de A se conserva como historial, no se borra ni se pisa.
        Assert.NotNull(rowB);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Requested, rowA!.Status);
        Assert.Equal(WhatsAppCoexistenceSyncStatus.Requested, rowB!.Status);
    }

    private static WhatsAppEmbeddedOnboardingDto CreateOnboarding(WhatsAppEmbeddedOnboardingMode mode, DateTime? startedAtUtc = null)
        => new()
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = 84,
            OnboardingMode = mode,
            Status = WhatsAppEmbeddedOnboardingStatus.Ready,
            StartedAtUtc = startedAtUtc ?? DateTime.UtcNow.AddMinutes(-10)
        };

    private sealed class MemorySyncStore : IWhatsAppCoexistenceSyncStore
    {
        // Misma identidad que el store real: one-shot por (IdBase, IdOnboarding, PhoneNumberId, SyncType).
        public Dictionary<(int IdBase, Guid IdOnboarding, string PhoneNumberId, WhatsAppCoexistenceSyncType SyncType), WhatsAppCoexistenceSyncDto> Rows { get; } = [];

        public Task<bool> TryReserveAsync(int idBase, string phoneNumberId, Guid idOnboarding, WhatsAppCoexistenceSyncType syncType, WhatsAppCoexistenceSyncStatus initialStatus, DateTime nowUtc, CancellationToken ct = default)
        {
            var key = (idBase, idOnboarding, phoneNumberId, syncType);
            if (Rows.ContainsKey(key)) return Task.FromResult(false);
            Rows[key] = new WhatsAppCoexistenceSyncDto
            {
                IdBase = idBase,
                PhoneNumberId = phoneNumberId,
                IdOnboarding = idOnboarding,
                SyncType = syncType,
                Status = initialStatus,
                CreatedAtUtc = nowUtc,
                ModifiedAtUtc = nowUtc
            };
            return Task.FromResult(true);
        }

        public Task MarkRequestedAsync(int idBase, string phoneNumberId, Guid idOnboarding, WhatsAppCoexistenceSyncType syncType, string requestId, DateTime requestedAtUtc, CancellationToken ct = default)
        {
            var key = (idBase, idOnboarding, phoneNumberId, syncType);
            if (Rows.TryGetValue(key, out var row) && row.Status == WhatsAppCoexistenceSyncStatus.Pending)
            {
                row.Status = WhatsAppCoexistenceSyncStatus.Requested;
                row.RequestId = requestId;
                row.RequestedAtUtc = requestedAtUtc;
            }
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(int idBase, string phoneNumberId, Guid idOnboarding, WhatsAppCoexistenceSyncType syncType, string errorCode, string errorSummary, CancellationToken ct = default)
        {
            var key = (idBase, idOnboarding, phoneNumberId, syncType);
            if (Rows.TryGetValue(key, out var row) && row.Status == WhatsAppCoexistenceSyncStatus.Pending)
            {
                row.Status = WhatsAppCoexistenceSyncStatus.Failed;
                row.ErrorCode = errorCode;
                row.ErrorSummary = errorSummary;
            }
            return Task.CompletedTask;
        }

        public Task MarkInProgressAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkCompletedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, DateTime completedAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkDeclinedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, string errorCode, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<WhatsAppCoexistenceSyncDto?> GetAsync(int idBase, string phoneNumberId, Guid idOnboarding, WhatsAppCoexistenceSyncType syncType, CancellationToken ct = default)
            => Task.FromResult(Rows.GetValueOrDefault((idBase, idOnboarding, phoneNumberId, syncType)));

        public Task<IReadOnlyList<WhatsAppCoexistenceSyncDto>> GetForBaseAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WhatsAppCoexistenceSyncDto>>(Rows.Values.Where(x => x.IdBase == idBase).ToArray());
    }

    private sealed class MemoryManagementClient : IMetaWhatsAppManagementClient
    {
        public List<(WhatsAppCoexistenceSyncType SyncType, string PhoneNumberId)> Requests { get; } = [];
        public WhatsAppCoexistenceSyncType? ThrowFor { get; set; }
        private int _requestCounter;

        public Task<MetaSmbAppDataSyncResult> RequestSmbAppDataSyncAsync(string phoneNumberId, WhatsAppCoexistenceSyncType syncType, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
        {
            Requests.Add((syncType, phoneNumberId));
            if (ThrowFor == syncType)
                throw new MetaWhatsAppManagementException("META_ERROR", true, false, "fallo simulado");
            return Task.FromResult(new MetaSmbAppDataSyncResult($"req-{++_requestCounter}"));
        }

        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pinReference, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
