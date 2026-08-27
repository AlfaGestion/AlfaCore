using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Repositories;
using AlfaCore.Services;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppEmbeddedSignupFoundationTests
{
    private const string FixtureWabaId = "2162413124698164";
    private const string FixturePhoneNumberId = "1285867614609340";

    [Fact]
    public async Task OnboardingCreation_PersistsHashTenantUserAndExpiration()
    {
        var store = new CapturingOnboardingStore();
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new FakeMetaOAuthClient(),
            new FakeCredentialVault(),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowedBaseIds = [106], OnboardingExpirationMinutes = 30 }));

        var result = await orchestrator.StartAsync(new WhatsAppEmbeddedStartRequest(106, "ALFANET", "Eve", WhatsAppEmbeddedOnboardingMode.Standard));

        Assert.NotNull(store.Created);
        Assert.Equal(result.IdOnboarding, store.Created.IdOnboarding);
        Assert.Equal(106, store.Created.IdBase);
        Assert.Equal("Eve", store.Created.UsuarioIniciador);
        Assert.NotEqual(result.State, store.Created.StateHash);
        Assert.Equal(store.Created.StartedAtUtc.AddMinutes(30), store.Created.ExpiresAtUtc);
    }

    [Fact]
    public async Task OnboardingCreationPersistsSelectedMode()
    {
        var store = new CapturingOnboardingStore();
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new FakeMetaOAuthClient(),
            new FakeCredentialVault(),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowedBaseIds = [106], OnboardingExpirationMinutes = 30 }));

        await orchestrator.StartAsync(new WhatsAppEmbeddedStartRequest(
            106,
            "ALFANET",
            "Eve",
            WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence));

        Assert.Equal(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, store.Created?.OnboardingMode);
    }

    [Fact]
    public void ConnectionChoiceMapping_IsExplicitAndNeverLeaksBetweenAttempts()
    {
        var selection = new WhatsAppConnectionChoiceSelection();

        selection.Begin();
        selection.Select(WhatsAppConnectionChoice.ExistingWhatsAppBusiness);
        selection.Clear();
        selection.Begin();
        selection.Select(WhatsAppConnectionChoice.NewWhatsApp);
        Assert.Equal(WhatsAppEmbeddedOnboardingMode.Standard, WhatsAppConnectionChoiceMapper.ToOnboardingMode(selection.Consume()));
        Assert.Null(selection.Selected);

        selection.Begin();
        selection.Select(WhatsAppConnectionChoice.NewWhatsApp);
        selection.Clear();
        selection.Begin();
        selection.Select(WhatsAppConnectionChoice.ExistingWhatsAppBusiness);
        Assert.Equal(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, WhatsAppConnectionChoiceMapper.ToOnboardingMode(selection.Consume()));
        Assert.Null(selection.Selected);
        Assert.Throws<InvalidOperationException>(() => selection.Consume());
    }

    [Fact]
    public void State_IsRandomHashedAndNeverPersistedAsPlainText()
    {
        var protector = new WhatsAppEmbeddedSignupStateProtector();
        var first = protector.Create();
        var second = protector.Create();
        Assert.NotEqual(first.State, second.State);
        Assert.Equal(64, first.Hash.Length);
        Assert.NotEqual(first.State, first.Hash);
        Assert.Equal(first.Hash, protector.Hash(first.State));
    }

    [Fact]
    public void State_IsSingleUseBoundToUserAndExpiration()
    {
        var now = DateTime.UtcNow;
        var item = new WhatsAppEmbeddedOnboardingDto
        {
            Status = WhatsAppEmbeddedOnboardingStatus.Started,
            StateHash = "HASH",
            UsuarioIniciador = "Eve",
            ExpiresAtUtc = now.AddMinutes(5)
        };
        Assert.True(WhatsAppEmbeddedSignupStateMachine.CanConsumeState(item, "HASH", "eve", now));
        item.StateConsumedAtUtc = now;
        Assert.False(WhatsAppEmbeddedSignupStateMachine.CanConsumeState(item, "HASH", "eve", now));
        Assert.False(WhatsAppEmbeddedSignupStateMachine.CanConsumeState(new() { Status = WhatsAppEmbeddedOnboardingStatus.Started, StateHash = "HASH", UsuarioIniciador = "Eve", ExpiresAtUtc = now }, "HASH", "Eve", now));
    }

    [Fact]
    public void StateMachine_AllowsExpectedTransitionAndRejectsInvalidTransition()
    {
        Assert.True(WhatsAppEmbeddedSignupStateMachine.CanTransition(WhatsAppEmbeddedOnboardingStatus.Started, WhatsAppEmbeddedOnboardingStatus.Authorized));
        Assert.Throws<InvalidOperationException>(() => WhatsAppEmbeddedSignupStateMachine.EnsureTransition(WhatsAppEmbeddedOnboardingStatus.Ready, WhatsAppEmbeddedOnboardingStatus.Started));
    }

    [Fact]
    public void Expiration_DoesNotExpireReadyOnboarding()
    {
        var now = DateTime.UtcNow;
        Assert.True(WhatsAppEmbeddedSignupStateMachine.IsExpired(new() { Status = WhatsAppEmbeddedOnboardingStatus.Started, ExpiresAtUtc = now.AddSeconds(-1) }, now));
        Assert.False(WhatsAppEmbeddedSignupStateMachine.IsExpired(new() { Status = WhatsAppEmbeddedOnboardingStatus.Ready, ExpiresAtUtc = now.AddSeconds(-1) }, now));
    }

    [Fact]
    public void RetryScheduling_UsesCappedExponentialBackoff()
    {
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddSeconds(30), WhatsAppEmbeddedSignupStateMachine.ScheduleRetry(now, 0, 30, 1800));
        Assert.Equal(now.AddSeconds(1800), WhatsAppEmbeddedSignupStateMachine.ScheduleRetry(now, 20, 30, 1800));
    }

    [Theory]
    [InlineData(null, 10, WhatsAppAssetOwnershipResult.Reserved)]
    [InlineData(10, 10, WhatsAppAssetOwnershipResult.AlreadyOwnedByBase)]
    [InlineData(10, 20, WhatsAppAssetOwnershipResult.Conflict)]
    public void OwnershipPolicy_IsIdempotentAndBlocksCrossTenant(int? existingBase, int requestedBase, WhatsAppAssetOwnershipResult expected)
        => Assert.Equal(expected, WhatsAppAssetOwnershipPolicy.Evaluate(existingBase, requestedBase));

    [Fact]
    public void FixturePhoneRediscoveryInSameBase_DoesNotDuplicateConceptually()
    {
        Assert.NotEmpty(FixtureWabaId);
        Assert.NotEmpty(FixturePhoneNumberId);
        Assert.Equal(WhatsAppAssetOwnershipResult.AlreadyOwnedByBase, WhatsAppAssetOwnershipPolicy.Evaluate(106, 106));
    }

    [Fact]
    public async Task ConcurrentOwnershipDecisions_NeverAuthorizeAnotherBase()
    {
        var decisions = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            WhatsAppAssetOwnershipPolicy.Evaluate(106, index % 2 == 0 ? 106 : 205))));
        Assert.All(decisions.Where((_, index) => index % 2 == 0), x => Assert.Equal(WhatsAppAssetOwnershipResult.AlreadyOwnedByBase, x));
        Assert.All(decisions.Where((_, index) => index % 2 != 0), x => Assert.Equal(WhatsAppAssetOwnershipResult.Conflict, x));
    }

    [Fact]
    public void ActionRequired_IsSeparateFromTechnicalFailure()
    {
        Assert.True(WhatsAppEmbeddedSignupStateMachine.CanTransition(WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment, WhatsAppEmbeddedOnboardingStatus.ActionRequired));
        Assert.NotEqual(WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable);
        Assert.Equal("CUSTOMER_PAYMENT_SETUP_REQUIRED", WhatsAppEmbeddedErrorCodes.CustomerPaymentSetupRequired);
    }

    [Fact]
    public void Options_HaveNoProductionIdsHardcodedInDomainDefaults()
    {
        var options = new WhatsAppEmbeddedSignupOptions();
        Assert.Empty(options.AppId);
        Assert.Empty(options.BusinessPortfolioId);
        Assert.Empty(options.SystemUserId);
        Assert.Empty(options.EmbeddedSignupConfigId);
        Assert.Empty(options.AllowedBaseIds);
        Assert.False(options.IsAllowedForBase(84));
        Assert.Equal(WhatsAppEmbeddedSignupCreditMode.CustomerPaysMeta, options.CreditMode);
    }

    [Fact]
    public void RuntimeConfigurationUsesConfirmedEmbeddedSignupConfigId()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("src", "AlfaCore", "appsettings.json")));
        var configuredId = document.RootElement
            .GetProperty("WhatsAppEmbeddedSignup")
            .GetProperty("EmbeddedSignupConfigId")
            .GetString();

        Assert.Equal("1753413148641744", configuredId);
    }

    [Fact]
    public void LocalLauncher_IsPinnedToLocalDbAndBase84WithoutProductionFallback()
    {
        var launcher = File.ReadAllText(FindRepoFile("tools", "run-alfacore-es-local.ps1"));
        Assert.Contains("ALFA_CENTRAL_DEV", launcher, StringComparison.Ordinal);
        Assert.Contains("ALFACORE_ES_TENANT_DEV", launcher, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__AlfaCentral", launcher, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__AlfaGestion", launcher, StringComparison.Ordinal);
        Assert.Contains("WhatsAppEmbeddedSignup__AllowedBaseIds__0 = \"84\"", launcher, StringComparison.Ordinal);
        Assert.Contains("WhatsAppEmbeddedSignup__WorkerEnabled = \"false\"", launcher, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets list", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", launcher, StringComparison.OrdinalIgnoreCase);

        var fixture = File.ReadAllText(FindRepoFile("tools", "es-local", "fixtures", "inbound-text.json"));
        Assert.DoesNotContain("1547539197385596", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("1195619520311268", fixture, StringComparison.Ordinal);
    }

    [Fact]
    public void CoexistenceCannotEnterPhoneRegistration()
    {
        Assert.True(WhatsAppEmbeddedPipelinePolicy.CanRegisterPhone(WhatsAppEmbeddedOnboardingMode.Standard));
        Assert.False(WhatsAppEmbeddedPipelinePolicy.CanRegisterPhone(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence));
        Assert.Throws<InvalidOperationException>(() =>
            WhatsAppEmbeddedPipelinePolicy.EnsureCanRegisterPhone(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence));
    }

    [Fact]
    public void BothModesConvergeToTheSameConnectedNumberMetadata()
    {
        var standard = new WhatsAppConnectedNumberMetadata("phone-test", WhatsAppEmbeddedOnboardingMode.Standard);
        var coexistence = new WhatsAppConnectedNumberMetadata("phone-test", WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

        Assert.Equal(standard.PhoneNumberId, coexistence.PhoneNumberId);
        Assert.Equal("AlfaCore", standard.UserFacingConnection);
        Assert.Equal("WhatsApp Business + AlfaCore", coexistence.UserFacingConnection);
    }

    [Fact]
    public void HistoryIdempotencyKeyIsStable()
    {
        Assert.Equal(
            WhatsAppEmbeddedPipelinePolicy.BuildHistoryIdempotencyKey(106, "phone-test", "event-test"),
            WhatsAppEmbeddedPipelinePolicy.BuildHistoryIdempotencyKey(106, "phone-test", "event-test"));
    }

    [Fact]
    public void PublicOnboardingModel_DoesNotExposeSecretsOrPin()
    {
        var propertyNames = typeof(WhatsAppEmbeddedOnboardingDto).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(propertyNames, x => x.Contains("AccessToken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Equals("Pin", StringComparison.OrdinalIgnoreCase));

        var json = JsonSerializer.Serialize(new WhatsAppEmbeddedOnboardingDto { TokenReference = "vault-reference" });
        Assert.DoesNotContain("access_token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization_code", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(typeof(WhatsAppEmbeddedAuthorizationCallback).GetProperties(), property =>
            property.Name.Contains("Mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ErrorLogger_StoresOnlyAllowlistedSanitizedMetadata()
    {
        var repository = new CapturingAuxErrRepository();
        var logger = new WhatsAppEmbeddedSignupErrorLogger(repository);
        const string secret = "token-super-secreto";

        var incidentId = await logger.LogAsync(Guid.NewGuid(), 106, $"exchange/{secret}", $"oauth:{secret}", "waba-123", "phone-456", -2);

        Assert.NotNull(repository.Entry);
        Assert.Contains(incidentId, repository.Entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, repository.Entry.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, repository.Entry.SqlDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("123", repository.Entry.SqlDetail, StringComparison.Ordinal);
        Assert.Contains("456", repository.Entry.SqlDetail, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("No se encontró el archivo desde el directorio de pruebas.");
    }

    private sealed class CapturingOnboardingStore : IWhatsAppEmbeddedSignupStore
    {
        public WhatsAppEmbeddedOnboardingDto? Created { get; private set; }
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) { Created = onboarding; return Task.CompletedTask; }
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default) => Task.FromResult(Created);
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => Task.FromResult(Created?.IdBase == idBase ? Created : null);
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }


    private sealed class CapturingAuxErrRepository : IAuxErrRepository
    {
        public AuxErrEntry? Entry { get; private set; }
        public Task<int> InsertAsync(AuxErrEntry entry, CancellationToken ct = default)
        {
            Entry = entry;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeMetaOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default)
            => Task.FromResult(new MetaTokenExchangeResult(new WhatsAppCredentialReference("fake-reference"), null));
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeCredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.CompletedTask;
    }
}
