using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Regresión Base4271 (producción): PublicBaseUrl del tenant quedó persistido como
/// "http://localhost:5055/" (autoguardado desde NavigationManager.BaseUri al guardar la config de
/// WhatsApp en local -- ver ConversacionesConfiguracionEmbeddedSignupUiTests.
/// SaveWhatsAppConfigAsync_NeverAutoPersists_...). Como el valor NO estaba vacío, el fallback a
/// WhatsAppEmbeddedSignup:CallbackBaseUrl nunca se activaba (fail-closed correcto), pero el onboarding
/// quedaba reintentando SUBSCRIBING_WABAS para siempre como WORKER_STEP_FAILED/FAILED_RETRYABLE en vez
/// de terminar en un estado claro que pida revisión humana. Cubre las tres capas del fix: (1) el
/// servicio de configuración nunca vuelve a aceptar http/localhost/relativo como PublicBaseUrl de
/// WhatsApp, (2) el worker clasifica ese fallo como ACTION_REQUIRED sin consumir reintentos
/// automáticos, (3) el botón genérico "Conectar WhatsApp" no vuelve a ofrecerse mientras ya exista un
/// onboarding FAILED_RETRYABLE (el dominio lo rechazaría de forma confusa).
/// </summary>
public sealed class WhatsAppCallbackRoutingConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeWhatsAppPublicBaseUrl_Empty_IsAllowed_UsesGlobalFallback(string? value)
        => Assert.Equal(string.Empty, ConversacionesConfigService.NormalizeWhatsAppPublicBaseUrl(value));

    [Fact]
    public void NormalizeWhatsAppPublicBaseUrl_AbsoluteHttps_IsAllowed()
        => Assert.Equal("https://alfacentral.ddns.net", ConversacionesConfigService.NormalizeWhatsAppPublicBaseUrl("https://alfacentral.ddns.net/"));

    [Theory]
    [InlineData("http://localhost:5055/")]
    [InlineData("http://midominio.com/")]
    [InlineData("https://localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    public void NormalizeWhatsAppPublicBaseUrl_HttpOrLocalhostOrRelative_IsRejected(string value)
        => Assert.Throws<InvalidOperationException>(() => ConversacionesConfigService.NormalizeWhatsAppPublicBaseUrl(value));

    [Fact]
    public void CtaPolicy_FailedRetryable_DoesNotOfferTheGenericStartButton()
    {
        // El botón genérico ("Conectar WhatsApp"/"Conectar otro WhatsApp") dispara StartAsync, que
        // el dominio ahora rechaza mientras exista un FAILED_RETRYABLE -- ofrecerlo sería confuso. La
        // reconexión real vive en el panel de la conexión pendiente (Reintentar / Volver a conectar
        // con Meta), no en este botón.
        var state = WhatsAppEmbeddedSignupCtaPolicy.Resolve(true, 0, WhatsAppEmbeddedOnboardingStatus.FailedRetryable);

        Assert.False(state.ShowPrimaryAction);
        // Tampoco debe mostrar el tag "Configuración en curso" -- sería engañoso para un estado FAILED.
        Assert.False(state.IsBlockedByActiveOnboarding);
    }

    [Fact]
    public void CtaPolicy_ActionRequired_StillOffersTheGenericStartButton_UnaffectedByThisFix()
    {
        // No se amplía el bloqueo más allá de FAILED_RETRYABLE -- StartAsync no rechaza ACTION_REQUIRED
        // (tiene su propia salida explícita, CancelActionRequiredConfigurationAsync), así que la UI
        // tampoco debe empezar a ocultar el botón ahí.
        var state = WhatsAppEmbeddedSignupCtaPolicy.Resolve(true, 0, WhatsAppEmbeddedOnboardingStatus.ActionRequired);

        Assert.True(state.ShowPrimaryAction);
    }

    [Fact]
    public async Task StartAsync_BlocksNewOnboarding_WhileLatestIsFailedRetryable()
    {
        var store = new RoutingRepairStore();
        var existing = Seeded(4271, WhatsAppEmbeddedOnboardingStatus.FailedRetryable);
        store.Seed(existing);
        var orchestrator = BuildOrchestrator(store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.StartAsync(new WhatsAppEmbeddedStartRequest(4271, "ALFANET", "Eve", WhatsAppEmbeddedOnboardingMode.Standard)));

        Assert.Contains("reintento", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task StartAsync_IsAllowed_AfterSupersedeForReconnect()
    {
        var store = new RoutingRepairStore();
        var existing = Seeded(4271, WhatsAppEmbeddedOnboardingStatus.FailedRetryable);
        store.Seed(existing);
        var orchestrator = BuildOrchestrator(store);

        var superseded = await orchestrator.SupersedeForReconnectAsync(existing.IdOnboarding, 4271);
        Assert.True(superseded);

        var result = await orchestrator.StartAsync(new WhatsAppEmbeddedStartRequest(4271, "ALFANET", "Eve", WhatsAppEmbeddedOnboardingMode.Standard));

        Assert.NotEqual(existing.IdOnboarding, result.IdOnboarding);
        var old = await store.GetAsync(existing.IdOnboarding);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.FailedFinal, old!.Status);
        Assert.Equal("SUPERSEDED_BY_RECONNECT", old.CurrentStep);
    }

    [Fact]
    public async Task Worker_CallbackRoutingConfigurationError_MarksActionRequired_WithoutConsumingRetry()
    {
        var item = Seeded(4271, WhatsAppEmbeddedOnboardingStatus.SubscribingWabas);
        item.RetryCount = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new WorkerFailureStore(item, cancellation);
        var orchestrator = new ThrowingOrchestrator(
            new WhatsAppCallbackRoutingConfigurationException("La Base pública HTTPS de WhatsApp no es válida."));
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => orchestrator)
            .AddScoped<IWhatsAppEmbeddedSignupErrorLogger, NullErrorLogger>()
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, WorkerEnabled = true, AllowedBaseIds = [4271], WorkerIntervalSeconds = 5 }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await store.Released.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.ActionRequired, item.Status);
        Assert.Equal(WhatsAppEmbeddedActionRequiredReason.CallbackRoutingConfigurationInvalid, item.ActionRequiredReason);
        Assert.Equal(WhatsAppEmbeddedErrorCodes.CallbackRoutingConfigurationInvalid, item.ErrorCode);
        // El mensaje al cliente no debe revelar el detalle interno (localhost, URL, tokens).
        Assert.DoesNotContain("localhost", item.ErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.False(store.MarkedRetryableFailure, "No debe consumir un reintento automático: es un fallo determinístico, no transitorio.");
        Assert.Equal(0, item.RetryCount);
        Assert.Null(item.NextAttemptUtc);
        Assert.Null(store.ReleasedNextAttemptUtc);
        // Ownership/Vault/token no se tocan -- MarkActionRequiredAsync sólo cambia Estado/Paso/Reason/ErrorSummary/IncidentId.
        Assert.Equal("vault-token", item.TokenReference);
    }

    [Fact]
    public async Task Worker_RoutingError_OnAlreadyRetriedOnboarding_StopsForGoodWithoutResettingHistory()
    {
        // Caso real de Base4271: el onboarding NO arrancaba en RetryCount=0 -- ya venía FAILED_RETRYABLE
        // con 6 reintentos automáticos consumidos (WORKER_STEP_FAILED genérico, antes de este fix) y un
        // NextAttemptUtc programado, vencido, listo para que el worker lo reclamara de nuevo. Este test
        // prueba la transición completa: reclamo real (filtro por Estado + NextAttemptUtc, no un fake
        // simplificado) -> falla por routing -> ACTION_REQUIRED -> un reclamo posterior YA NO lo agarra.
        var now = DateTime.UtcNow;
        var item = Seeded(4271, WhatsAppEmbeddedOnboardingStatus.FailedRetryable);
        item.RetryCount = 6;
        item.NextAttemptUtc = now.AddMinutes(-5); // vencido: si el Estado no lo excluyera, sería reclamable.
        var store = new RealisticClaimStore(item);

        var claimed = await store.ClaimNextForBasesAsync("worker-1", [4271], now, now.AddMinutes(2));
        Assert.Equal(item.IdOnboarding, claimed?.IdOnboarding);
        // Libero este claim de sanity-check -- el worker de abajo reclama por su cuenta con su propio
        // workerId; si dejo la fila marcada como reclamada acá, el worker real nunca la va a agarrar.
        await store.ReleaseClaimAsync(item.IdOnboarding, "worker-1", null);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnReleased = () => { releaseGate.TrySetResult(); cancellation.Cancel(); };
        var orchestrator = new ThrowingOrchestrator(
            new WhatsAppCallbackRoutingConfigurationException("La Base pública HTTPS de WhatsApp no es válida."));
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => orchestrator)
            .AddScoped<IWhatsAppEmbeddedSignupErrorLogger, NullErrorLogger>()
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, WorkerEnabled = true, AllowedBaseIds = [4271], WorkerIntervalSeconds = 5 }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        // El worker reclama de nuevo por su cuenta (ClaimNextForBasesAsync), procesa, falla y libera --
        // no reuso el "claimed" de arriba, que sólo demostró que ERA reclamable antes del fix.
        await worker.StartAsync(CancellationToken.None);
        await releaseGate.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.ActionRequired, item.Status);
        Assert.Equal(WhatsAppEmbeddedActionRequiredReason.CallbackRoutingConfigurationInvalid, item.ActionRequiredReason);
        Assert.Equal(WhatsAppEmbeddedErrorCodes.CallbackRoutingConfigurationInvalid, item.ErrorCode);
        // RetryCount=6 se preserva como evidencia histórica -- ACTION_REQUIRED nunca lo resetea ni lo incrementa.
        Assert.Equal(6, item.RetryCount);
        // MarkActionRequiredAsync debe cancelar el scheduling previo: nada de NextAttemptUtc colgado.
        Assert.Null(item.NextAttemptUtc);
        // Ownership/Vault: MarkActionRequiredAsync nunca los toca -- siguen intactos.
        Assert.Equal("vault-token", item.TokenReference);

        // El worker vuelve a consultar inmediatamente después: ya NO es elegible (Estado=ACTION_REQUIRED
        // no está en el filtro de ClaimNextForBasesAsync, sin importar NextAttemptUtc).
        var reclaimed = await store.ClaimNextForBasesAsync("worker-2", [4271], now.AddMinutes(10), now.AddMinutes(12));
        Assert.Null(reclaimed);
    }

    [Fact]
    public async Task Worker_TransientMetaError_StillSchedulesRetry_UnaffectedByThisFix()
    {
        var item = Seeded(4271, WhatsAppEmbeddedOnboardingStatus.SubscribingWabas);
        item.RetryCount = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new WorkerFailureStore(item, cancellation);
        var orchestrator = new ThrowingOrchestrator(
            new MetaWhatsAppManagementException("1", true, false, "Timeout transitorio de Meta."));
        var services = new ServiceCollection()
            .AddScoped<IWhatsAppEmbeddedSignupStore>(_ => store)
            .AddScoped<IWhatsAppEmbeddedSignupOrchestrator>(_ => orchestrator)
            .AddScoped<IWhatsAppEmbeddedSignupErrorLogger, NullErrorLogger>()
            .BuildServiceProvider();
        var worker = new WhatsAppEmbeddedSignupHostedService(
            services,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, WorkerEnabled = true, AllowedBaseIds = [4271], WorkerIntervalSeconds = 5, RetryInitialDelaySeconds = 30, RetryMaxDelaySeconds = 60, MaxRetryCount = 8 }),
            NullLogger<WhatsAppEmbeddedSignupHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await store.Released.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(store.MarkedRetryableFailure);
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.FailedRetryable, item.Status);
        Assert.NotNull(store.ReleasedNextAttemptUtc);
    }

    private static WhatsAppEmbeddedOnboardingDto Seeded(int idBase, WhatsAppEmbeddedOnboardingStatus status)
    {
        var now = DateTime.UtcNow;
        return new WhatsAppEmbeddedOnboardingDto
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = idBase,
            IdCliente = "ALFANET",
            UsuarioIniciador = "Eve",
            StateHash = "hash",
            Status = status,
            OnboardingMode = WhatsAppEmbeddedOnboardingMode.Standard,
            CurrentStep = status.ToString().ToUpperInvariant(),
            TokenReference = "vault-token",
            StartedAtUtc = now.AddMinutes(-5),
            ExpiresAtUtc = now.AddMinutes(25),
            ModifiedAtUtc = now.AddMinutes(-1)
        };
    }

    private static WhatsAppEmbeddedSignupOrchestrator BuildOrchestrator(RoutingRepairStore store)
        => new(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new NoopMetaOAuthClient(),
            new NoopCredentialVault(),
            Options.Create(new WhatsAppEmbeddedSignupOptions
            {
                Enabled = true,
                AllowedBaseIds = [4271],
                AppId = "test-app-id",
                BusinessPortfolioId = "test-business-id",
                SystemUserId = "test-system-user-id",
                EmbeddedSignupConfigId = "test-config-id",
                GraphApiVersion = "v26.0",
                GraphBaseUrl = "https://graph.facebook.com",
                OnboardingExpirationMinutes = 30
            }));

    /// <summary>Doble mínimo para StartAsync/SupersedeForReconnectAsync -- no reproduce ExpireStaleStartedAsync
    /// porque estos onboardings seed no están STARTED (ver ExpiringOnboardingStore en
    /// WhatsAppEmbeddedSignupExpirationTests.cs para ese caso).</summary>
    private sealed class RoutingRepairStore : IWhatsAppEmbeddedSignupStore
    {
        private readonly Dictionary<Guid, WhatsAppEmbeddedOnboardingDto> _items = [];
        public List<Guid> Created { get; } = [];

        public void Seed(WhatsAppEmbeddedOnboardingDto item) => _items[item.IdOnboarding] = item;

        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default)
        {
            _items[onboarding.IdOnboarding] = onboarding;
            Created.Add(onboarding.IdOnboarding);
            return Task.CompletedTask;
        }

        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default)
            => Task.FromResult(_items.GetValueOrDefault(idOnboarding));

        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(_items.Values.Where(x => x.IdBase == idBase).OrderByDescending(x => x.StartedAtUtc).FirstOrDefault());

        public Task<bool> SupersedeFailedForReconnectAsync(Guid idOnboarding, int idBase, DateTime nowUtc, CancellationToken ct = default)
        {
            if (!_items.TryGetValue(idOnboarding, out var item) || item.IdBase != idBase || item.Status != WhatsAppEmbeddedOnboardingStatus.FailedRetryable)
                return Task.FromResult(false);
            item.Status = WhatsAppEmbeddedOnboardingStatus.FailedFinal;
            item.CurrentStep = "SUPERSEDED_BY_RECONNECT";
            return Task.FromResult(true);
        }

        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, string errorCode = "", CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// A diferencia de las demás dobles de este archivo (que fijan el estado inicial directamente),
    /// reproduce el FILTRO real de ClaimNextInternalAsync (Estado IN lista-de-elegibles AND
    /// (NextAttemptUtc IS NULL OR NextAttemptUtc &lt;= ahora)) -- necesario para demostrar de verdad
    /// que, tras MarkActionRequiredAsync, un segundo reclamo ya no agarra la fila (no alcanza con
    /// "nunca se volvió a llamar _claimed", hay que probar que el filtro por Estado lo excluye).
    /// </summary>
    private sealed class RealisticClaimStore(WhatsAppEmbeddedOnboardingDto item) : IWhatsAppEmbeddedSignupStore
    {
        private static readonly HashSet<WhatsAppEmbeddedOnboardingStatus> Claimable =
        [
            WhatsAppEmbeddedOnboardingStatus.Authorized, WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets,
            WhatsAppEmbeddedOnboardingStatus.ValidatingOwnership, WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess,
            WhatsAppEmbeddedOnboardingStatus.SubscribingWabas, WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment,
            WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones, WhatsAppEmbeddedOnboardingStatus.RegisteringPhones,
            WhatsAppEmbeddedOnboardingStatus.Importing, WhatsAppEmbeddedOnboardingStatus.SyncingHistory,
            WhatsAppEmbeddedOnboardingStatus.SyncingContacts, WhatsAppEmbeddedOnboardingStatus.FailedRetryable
        ];

        private bool _claimedOut;
        public Action? OnReleased { get; set; }

        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextForBasesAsync(string workerId, IReadOnlyCollection<int> allowedBaseIds, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
        {
            if (_claimedOut || !allowedBaseIds.Contains(item.IdBase) || !Claimable.Contains(item.Status))
                return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
            if (item.NextAttemptUtc is { } next && next > nowUtc)
                return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
            _claimedOut = true;
            return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        }

        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, string errorCode = "", CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            item.Status = WhatsAppEmbeddedOnboardingStatus.ActionRequired;
            item.CurrentStep = "ACTION_REQUIRED";
            item.ActionRequiredReason = reason;
            item.ErrorSummary = summary;
            item.ErrorCode = errorCode;
            item.NextAttemptUtc = null; // mismo comportamiento que UpdateFieldsAsync/MarkActionRequiredAsync real.
            return Task.CompletedTask;
        }

        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            _claimedOut = false;
            OnReleased?.Invoke();
            return Task.CompletedTask;
        }

        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default) => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Reproduce el flujo real del worker (ClaimNextForBasesAsync -> orquestador que tira ->
    /// HandleItemFailureAsync) igual que FailureWorkerStore en WhatsAppEmbeddedSignupWorkerGateTests.cs,
    /// pero además registra ActionRequiredReason/ErrorSummary para poder verificarlos acá.</summary>
    private sealed class WorkerFailureStore(WhatsAppEmbeddedOnboardingDto item, CancellationTokenSource cancellation) : IWhatsAppEmbeddedSignupStore
    {
        private bool _claimed;
        public bool MarkedRetryableFailure { get; private set; }
        public bool MarkedActionRequired { get; private set; }
        public DateTime? ReleasedNextAttemptUtc { get; private set; }
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextForBasesAsync(string workerId, IReadOnlyCollection<int> allowedBaseIds, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
        {
            if (_claimed || !allowedBaseIds.Contains(item.IdBase))
                return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
            _claimed = true;
            return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        }

        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, string errorCode = "", CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            item.Status = WhatsAppEmbeddedOnboardingStatus.ActionRequired;
            item.CurrentStep = "ACTION_REQUIRED";
            item.ActionRequiredReason = reason;
            item.ErrorSummary = summary;
            item.ErrorCode = errorCode;
            item.NextAttemptUtc = null;
            MarkedActionRequired = true;
            return Task.CompletedTask;
        }

        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            item.Status = WhatsAppEmbeddedOnboardingStatus.FailedRetryable;
            item.CurrentStep = "RETRY_SCHEDULED";
            item.RetryCount++;
            MarkedRetryableFailure = true;
            return Task.CompletedTask;
        }

        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default)
        {
            Assert.Equal(item.IdOnboarding, idOnboarding);
            ReleasedNextAttemptUtc = nextAttemptUtc;
            Released.SetResult();
            cancellation.Cancel();
            return Task.CompletedTask;
        }

        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default) => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(item);
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingOrchestrator(Exception exception) : IWhatsAppEmbeddedSignupOrchestrator
    {
        public Task ProcessNextStepAsync(Guid idOnboarding, CancellationToken ct = default) => throw exception;
        public Task<WhatsAppEmbeddedStartResult> StartAsync(WhatsAppEmbeddedStartRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HandleAuthorizationCallbackAsync(WhatsAppEmbeddedAuthorizationCallback callback, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HandleCancellationAsync(Guid idOnboarding, int idBase, string state, string usuario, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedStatusView?> GetStatusAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedStatusView?> GetLatestStatusForBaseAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RetryAsync(WhatsAppEmbeddedRetryRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NullErrorLogger : IWhatsAppEmbeddedSignupErrorLogger
    {
        public Task<string> LogAsync(Guid idOnboarding, int idBase, string step, string errorCode, string? wabaId, string? phoneNumberId, int retryCount, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }

    private sealed class NoopMetaOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoopCredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.CompletedTask;
    }
}
