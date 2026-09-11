using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppEmbeddedSignupWorkerGateTests
{
    [Fact]
    public async Task WebhookRoutingDisabled_AdvancesOperationalPipelineWithoutSubscription()
    {
        var item = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess);
        var store = new MutableStore(item);
        var management = new RoutingSpyManagementClient();
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new ValidOAuthClient(),
            new CredentialVault(),
            new PhonePinVault(),
            management,
            new OwnershipStore(),
            new ErrorLogger(),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowedBaseIds = [84], WebhookRoutingEnabled = false }));

        await orchestrator.ProcessNextStepAsync(item.IdOnboarding);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment, item.Status);

        await orchestrator.ProcessNextStepAsync(item.IdOnboarding);
        await orchestrator.ProcessNextStepAsync(item.IdOnboarding);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Importing, item.Status);
        Assert.Equal("READY_FOR_OPERATIONAL_UPSERT", item.CurrentStep);
        Assert.Equal(0, management.SubscriptionCalls);
    }

    [Fact]
    public async Task WorkerClaimsOnlyAllowedAuthorizedOnboarding()
    {
        var allowed = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Authorized);
        var outside = CreateOnboarding(106, WhatsAppEmbeddedOnboardingStatus.Authorized);
        var ready = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Ready);
        var cancelled = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Cancelled);
        var failedFinal = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.FailedFinal);
        var expired = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Expired);
        var started = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Started, tokenReference: string.Empty);
        var store = new WorkerStore([outside, ready, cancelled, failedFinal, expired, started, allowed]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var orchestrator = new CancellingOrchestrator(cancellation);
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => orchestrator)
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, WorkerEnabled = true, AllowedBaseIds = [84], WorkerIntervalSeconds = 5 }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await orchestrator.Completed.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal([84], store.AllowedBases);
        Assert.Equal([allowed.IdOnboarding], store.Claimed);
        Assert.Equal([allowed.IdOnboarding], orchestrator.Processed);
        Assert.Empty(orchestrator.GraphWrites);
        Assert.DoesNotContain(outside.IdOnboarding, store.Claimed);
        Assert.DoesNotContain(ready.IdOnboarding, store.Claimed);
        Assert.DoesNotContain(cancelled.IdOnboarding, store.Claimed);
        Assert.DoesNotContain(failedFinal.IdOnboarding, store.Claimed);
        Assert.DoesNotContain(expired.IdOnboarding, store.Claimed);
        Assert.DoesNotContain(started.IdOnboarding, store.Claimed);
    }

    [Fact]
    public async Task WorkerSchedulesRetryAndReleasesLeaseWhenOperationalImportFails()
    {
        var item = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Importing);
        item.CurrentStep = "READY_FOR_OPERATIONAL_UPSERT";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new FailureWorkerStore(item, cancellation);
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => new CancellingOrchestrator(cancellation))
            .AddScoped<IWhatsAppEmbeddedOperationalImportService, ThrowingImporter>()
            .AddScoped<IWhatsAppEmbeddedSignupErrorLogger, ErrorLogger>()
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions
            {
                Enabled = true,
                WorkerEnabled = true,
                AllowedBaseIds = [84],
                WorkerIntervalSeconds = 5,
                RetryInitialDelaySeconds = 30,
                RetryMaxDelaySeconds = 60
            }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await store.Released.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(store.ScheduledImportRetry);
        Assert.True(store.ReleasedLease);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Importing, item.Status);
        Assert.Equal("READY_FOR_OPERATIONAL_UPSERT", item.CurrentStep);
        Assert.True(store.NextAttemptUtc > DateTime.UtcNow.AddSeconds(20));
    }

    [Fact]
    public async Task WorkerTreatsMeta80008AsRateLimitAndPreservesOperationalImport()
    {
        var item = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Importing);
        item.CurrentStep = "READY_FOR_OPERATIONAL_UPSERT";
        item.RetryCount = 1;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new FailureWorkerStore(item, cancellation);
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => new CancellingOrchestrator(cancellation))
            .AddScoped<IWhatsAppEmbeddedOperationalImportService, RateLimitedImporter>()
            .AddScoped<IWhatsAppEmbeddedSignupErrorLogger, ErrorLogger>()
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, WorkerEnabled = true, AllowedBaseIds = [84], WorkerIntervalSeconds = 5 }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        var before = DateTime.UtcNow;
        await worker.StartAsync(CancellationToken.None);
        await store.Released.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(store.ScheduledImportRetry);
        Assert.False(store.MarkedRetryableFailure);
        Assert.True(store.ReleasedLease);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Importing, item.Status);
        Assert.Equal("READY_FOR_OPERATIONAL_UPSERT", item.CurrentStep);
        Assert.True(store.NextAttemptUtc >= before.AddMinutes(30));
    }

    [Fact]
    public async Task WorkerClaimsLegacyCoexistenceImportAndInvokesOperationalImporter()
    {
        var item = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.Importing);
        item.OnboardingMode = WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence;
        item.CurrentStep = "READY_FOR_IMPORT_APPROVAL";
        var store = new WorkerStore([item]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var importer = new CompletingImporter(cancellation);
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => new CancellingOrchestrator(cancellation))
            .AddScoped<IWhatsAppEmbeddedOperationalImportService>(_ => importer)
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, WorkerEnabled = true, AllowedBaseIds = [84], WorkerIntervalSeconds = 5 }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await importer.Completed.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal([item.IdOnboarding], store.Claimed);
        Assert.Equal([item.IdOnboarding], importer.Imported);
    }

    [Fact]
    public async Task ControlledRecoveryFromFailedFinal_ClaimsOnlyOperationalImportWithoutDiscoveryOrGraphWrites()
    {
        var item = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.FailedFinal);
        item.OnboardingMode = WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence;
        item.CurrentStep = "FAILED";
        var store = new RecoveryWorkerStore(item);
        store.ApplyControlledRecovery();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var importer = new CompletingImporter(cancellation);
        var orchestrator = new FailingOrchestrator();
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => orchestrator)
            .AddScoped<IWhatsAppEmbeddedOperationalImportService>(_ => importer)
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, WorkerEnabled = true, AllowedBaseIds = [84], WebhookRoutingEnabled = false, WorkerIntervalSeconds = 5 }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await importer.Completed.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Importing, item.Status);
        Assert.Equal("READY_FOR_OPERATIONAL_UPSERT", item.CurrentStep);
        Assert.Equal([item.IdOnboarding], store.Claimed);
        Assert.Equal([item.IdOnboarding], importer.Imported);
        Assert.Equal(0, orchestrator.ProcessNextStepCalls);
    }

    [Fact]
    public async Task Retry_ResumesRecoverableFailureFromAuthorized()
    {
        var item = CreateOnboarding(84, WhatsAppEmbeddedOnboardingStatus.FailedRetryable);
        var store = new MutableStore(item);
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new ValidOAuthClient(),
            new CredentialVault(),
            new PhonePinVault(),
            new RoutingSpyManagementClient(),
            new OwnershipStore(),
            new ErrorLogger(),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowedBaseIds = [84] }));

        await orchestrator.RetryAsync(new WhatsAppEmbeddedRetryRequest(item.IdOnboarding, item.UsuarioIniciador));

        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Authorized, item.Status);
        Assert.Equal("RETRYING", item.CurrentStep);
    }

    private static WhatsAppEmbeddedOnboardingDto CreateOnboarding(int idBase, WhatsAppEmbeddedOnboardingStatus status, string tokenReference = "vault-token")
        => new()
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = idBase,
            IdCliente = "ALFANET",
            UsuarioIniciador = "test",
            OnboardingMode = WhatsAppEmbeddedOnboardingMode.Standard,
            Status = status,
            CurrentStep = status.ToString(),
            TokenReference = tokenReference,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };

    private sealed class WorkerStore(IReadOnlyList<WhatsAppEmbeddedOnboardingDto> items) : IWhatsAppEmbeddedSignupStore
    {
        public IReadOnlyList<int> AllowedBases { get; private set; } = [];
        public List<Guid> Claimed { get; } = [];
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new Xunit.Sdk.XunitException("The worker must use the allowlisted claim path.");
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextForBasesAsync(string workerId, IReadOnlyCollection<int> allowedBaseIds, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
        {
            AllowedBases = allowedBaseIds.Order().ToArray();
            var item = items.FirstOrDefault(x => allowedBaseIds.Contains(x.IdBase) && IsClaimable(x.Status));
            if (item is not null) Claimed.Add(item.IdOnboarding);
            return Task.FromResult(item);
        }
        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default) => Task.FromResult(items.FirstOrDefault(x => x.IdOnboarding == idOnboarding));
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CancellingOrchestrator(CancellationTokenSource cancellation) : IWhatsAppEmbeddedSignupOrchestrator
    {
        public List<Guid> Processed { get; } = [];
        public List<string> GraphWrites { get; } = [];
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ProcessNextStepAsync(Guid idOnboarding, CancellationToken ct = default) { Processed.Add(idOnboarding); Completed.SetResult(); cancellation.Cancel(); return Task.CompletedTask; }
        public Task<WhatsAppEmbeddedStartResult> StartAsync(WhatsAppEmbeddedStartRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HandleAuthorizationCallbackAsync(WhatsAppEmbeddedAuthorizationCallback callback, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HandleCancellationAsync(Guid idOnboarding, int idBase, string state, string usuario, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedStatusView?> GetStatusAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedStatusView?> GetLatestStatusForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RetryAsync(WhatsAppEmbeddedRetryRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class MutableStore(WhatsAppEmbeddedOnboardingDto item) : IWhatsAppEmbeddedSignupStore
    {
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default) => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) { Assert.Equal(item.Status, expectedStatus); item.Status = nextStatus; item.CurrentStep = currentStep; return Task.CompletedTask; }
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FailureWorkerStore(WhatsAppEmbeddedOnboardingDto item, CancellationTokenSource cancellation) : IWhatsAppEmbeddedSignupStore
    {
        private bool _claimed;
        public bool MarkedRetryableFailure { get; private set; }
        public bool ScheduledImportRetry { get; private set; }
        public bool ReleasedLease { get; private set; }
        public DateTime? NextAttemptUtc { get; private set; }
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextForBasesAsync(string workerId, IReadOnlyCollection<int> allowedBaseIds, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
        {
            if (_claimed || !allowedBaseIds.Contains(item.IdBase))
                return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
            _claimed = true;
            return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        }

        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            item.Status = WhatsAppEmbeddedOnboardingStatus.FailedRetryable;
            item.CurrentStep = "RETRY_SCHEDULED";
            MarkedRetryableFailure = true;
            NextAttemptUtc = nextAttemptUtc;
            return Task.CompletedTask;
        }

        public Task ScheduleRetryAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus resumeStatus, string resumeStep, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            item.Status = resumeStatus;
            item.CurrentStep = resumeStep;
            ScheduledImportRetry = true;
            NextAttemptUtc = nextAttemptUtc;
            return Task.CompletedTask;
        }

        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            ReleasedLease = true;
            NextAttemptUtc = nextAttemptUtc;
            Released.SetResult();
            cancellation.Cancel();
            return Task.CompletedTask;
        }

        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default) => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppEmbeddedOnboardingDto>> GetPendingForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingImporter : IWhatsAppEmbeddedOperationalImportService
    {
        public Task<WhatsAppEmbeddedOperationalImportResult> CompleteForBaseAsync(Guid idOnboarding, int activeBaseId, CancellationToken ct = default)
            => throw new InvalidOperationException("Import failure for test.");
    }

    private sealed class RateLimitedImporter : IWhatsAppEmbeddedOperationalImportService
    {
        public Task<WhatsAppEmbeddedOperationalImportResult> CompleteForBaseAsync(Guid idOnboarding, int activeBaseId, CancellationToken ct = default)
            => throw new MetaWhatsAppManagementException("80008", true, false, "Rate limited.", httpStatusCode: 400);
    }

    private sealed class RecoveryWorkerStore(WhatsAppEmbeddedOnboardingDto item) : IWhatsAppEmbeddedSignupStore
    {
        private bool _claimed;
        public List<Guid> Claimed { get; } = [];

        public void ApplyControlledRecovery()
        {
            Assert.Equal(WhatsAppEmbeddedOnboardingStatus.FailedFinal, item.Status);
            Assert.Equal("FAILED", item.CurrentStep);
            item.Status = WhatsAppEmbeddedOnboardingStatus.Importing;
            item.CurrentStep = "READY_FOR_OPERATIONAL_UPSERT";
        }

        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextForBasesAsync(string workerId, IReadOnlyCollection<int> allowedBaseIds, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
        {
            if (_claimed || !allowedBaseIds.Contains(item.IdBase) || item.Status != WhatsAppEmbeddedOnboardingStatus.Importing)
                return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
            _claimed = true;
            Claimed.Add(item.IdOnboarding);
            return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        }

        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default) => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppEmbeddedOnboardingDto>> GetPendingForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new Xunit.Sdk.XunitException("The recovery must use the allowlisted claim path.");
    }

    private sealed class FailingOrchestrator : IWhatsAppEmbeddedSignupOrchestrator
    {
        public int ProcessNextStepCalls { get; private set; }
        public Task ProcessNextStepAsync(Guid idOnboarding, CancellationToken ct = default)
        {
            ProcessNextStepCalls++;
            throw new Xunit.Sdk.XunitException("A recovered IMPORTING onboarding must not enter the orchestrator.");
        }
        public Task<WhatsAppEmbeddedStartResult> StartAsync(WhatsAppEmbeddedStartRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HandleAuthorizationCallbackAsync(WhatsAppEmbeddedAuthorizationCallback callback, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HandleCancellationAsync(Guid idOnboarding, int idBase, string state, string usuario, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedStatusView?> GetStatusAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedStatusView?> GetLatestStatusForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RetryAsync(WhatsAppEmbeddedRetryRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CompletingImporter(CancellationTokenSource cancellation) : IWhatsAppEmbeddedOperationalImportService
    {
        public List<Guid> Imported { get; } = [];
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WhatsAppEmbeddedOperationalImportResult> CompleteForBaseAsync(Guid idOnboarding, int activeBaseId, CancellationToken ct = default)
        {
            Assert.Equal(84, activeBaseId);
            Imported.Add(idOnboarding);
            Completed.SetResult();
            cancellation.Cancel();
            return Task.FromResult(new WhatsAppEmbeddedOperationalImportResult([]));
        }
    }

    private sealed class ValidOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.FromResult(new MetaTokenInspectionResult(true, null, []));
    }

    private sealed class CredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class PhonePinVault : IWhatsAppPhonePinVault
    {
        public Task<WhatsAppPhonePinReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> pin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class OwnershipStore : IWhatsAppAssetOwnershipStore
    {
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => Task.FromResult(new WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult.Reserved, idBase, wabaId));
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => Task.FromResult(new WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult.Reserved, idBase, phoneNumberId));
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ErrorLogger : IWhatsAppEmbeddedSignupErrorLogger
    {
        public Task<string> LogAsync(Guid idOnboarding, int idBase, string step, string errorCode, string? wabaId, string? phoneNumberId, int retryCount, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }

    private sealed class RoutingSpyManagementClient : IMetaWhatsAppManagementClient
    {
        public int SubscriptionCalls { get; private set; }
        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MetaAuthorizedBusiness>>([new("business-84", "AlfaTest")]);
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MetaWabaAsset>>([new("waba-84", businessId, "AlfaTest")]);
        public Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) { SubscriptionCalls++; return Task.CompletedTask; }
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MetaPhoneAsset>>([new("phone-84", wabaId, "+1 555-320-1773", "AlfaTest", "CONNECTED", "GREEN", MetaPhoneRegistrationStatus.Registered)]);
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.FromResult(MetaPhoneRegistrationStatus.Registered);
        public Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pinReference, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.CompletedTask;
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.FromResult(MetaCustomerPaymentReadiness.Ready);
    }

    private static bool IsClaimable(WhatsAppEmbeddedOnboardingStatus status)
        => status is WhatsAppEmbeddedOnboardingStatus.Authorized or WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets or WhatsAppEmbeddedOnboardingStatus.ValidatingOwnership or WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess or WhatsAppEmbeddedOnboardingStatus.SubscribingWabas or WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment or WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones or WhatsAppEmbeddedOnboardingStatus.RegisteringPhones or WhatsAppEmbeddedOnboardingStatus.Importing or WhatsAppEmbeddedOnboardingStatus.SyncingHistory or WhatsAppEmbeddedOnboardingStatus.SyncingContacts or WhatsAppEmbeddedOnboardingStatus.FailedRetryable;
}
