using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppEmbeddedSignupSqlIntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlIntegrationFactAttribute.EnvironmentVariable)!;
    private static IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AlfaCentral"] = ConnectionString }).Build();

    [SqlIntegrationFact]
    public async Task Ownership_IsIdempotentCrossTenantSafeAndConcurrent()
    {
        var store = new WhatsAppAssetOwnershipStore(Configuration);
        var (baseA, baseB) = await GetTwoBaseIdsAsync();
        var suffix = DateTime.UtcNow.Ticks.ToString()[^14..];
        var waba = "99991" + suffix;
        var phone = "99992" + suffix;
        var concurrentWaba = "99993" + suffix;
        try
        {
            var first = await store.ReserveWabaAsync(waba, baseA, "9999001");
            Assert.Equal(WhatsAppAssetOwnershipResult.Reserved, first.Result);
            Assert.Equal(WhatsAppAssetOwnershipResult.AlreadyOwnedByBase, (await store.ReserveWabaAsync(waba, baseA, "9999001")).Result);
            Assert.Equal(WhatsAppAssetOwnershipResult.Conflict, (await store.ReserveWabaAsync(waba, baseB, "9999002")).Result);

            var concurrentWabas = await Task.WhenAll(
                store.ReserveWabaAsync(concurrentWaba, baseA, "9999001"),
                store.ReserveWabaAsync(concurrentWaba, baseB, "9999002"));
            Assert.Single(concurrentWabas, x => x.Result == WhatsAppAssetOwnershipResult.Reserved);
            Assert.Single(concurrentWabas, x => x.Result == WhatsAppAssetOwnershipResult.Conflict);

            var concurrentPhones = await Task.WhenAll(
                store.ReservePhoneAsync(phone, waba, baseA),
                store.ReservePhoneAsync(phone, waba, baseB));
            Assert.Single(concurrentPhones, x => x.Result == WhatsAppAssetOwnershipResult.Reserved);
            Assert.Single(concurrentPhones, x => x.Result == WhatsAppAssetOwnershipResult.Conflict);
        }
        finally
        {
            await CleanupOwnershipAsync([phone], [waba, concurrentWaba]);
        }
    }

    [SqlIntegrationFact]
    public async Task State_IsBoundExpiredAndSingleUseInSql()
    {
        var store = new WhatsAppEmbeddedSignupStore(Configuration);
        var (baseA, baseB) = await GetTwoBaseIdsAsync();
        var now = DateTime.UtcNow;
        var item = NewOnboarding(baseA, WhatsAppEmbeddedOnboardingStatus.Started, now.AddMinutes(10));
        item.OnboardingMode = WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence;
        var expired = NewOnboarding(baseA, WhatsAppEmbeddedOnboardingStatus.Started, now.AddSeconds(-1));
        try
        {
            await store.CreateAsync(item);
            Assert.Equal(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, (await store.GetAsync(item.IdOnboarding))?.OnboardingMode);
            Assert.Null(await store.ConsumeStateAsync(item.StateHash, baseA, "otro", now));
            Assert.Null(await store.ConsumeStateAsync(item.StateHash, baseB, item.UsuarioIniciador, now));
            Assert.NotNull(await store.ConsumeStateAsync(item.StateHash, baseA, item.UsuarioIniciador, now));
            Assert.Null(await store.ConsumeStateAsync(item.StateHash, baseA, item.UsuarioIniciador, now));

            await store.CreateAsync(expired);
            Assert.Null(await store.ConsumeStateAsync(expired.StateHash, baseA, expired.UsuarioIniciador, now));
        }
        finally
        {
            await CleanupOnboardingsAsync([item.IdOnboarding, expired.IdOnboarding]);
        }
    }

    [SqlIntegrationFact]
    public async Task ModesAndAuthorizedCredential_ArePersistedExactlyInSql()
    {
        var store = new WhatsAppEmbeddedSignupStore(Configuration);
        var (baseA, _) = await GetTwoBaseIdsAsync();
        var now = DateTime.UtcNow;
        var standard = NewOnboarding(baseA, WhatsAppEmbeddedOnboardingStatus.Started, now.AddMinutes(10));
        standard.OnboardingMode = WhatsAppEmbeddedOnboardingMode.Standard;
        var coexistence = NewOnboarding(baseA, WhatsAppEmbeddedOnboardingStatus.Started, now.AddMinutes(10));
        coexistence.OnboardingMode = WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence;
        var reference = $"test-ref-{Guid.NewGuid():N}";
        try
        {
            await store.CreateAsync(standard);
            await store.CreateAsync(coexistence);

            Assert.Equal(WhatsAppEmbeddedOnboardingMode.Standard, (await store.GetAsync(standard.IdOnboarding))?.OnboardingMode);
            Assert.Equal(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, (await store.GetAsync(coexistence.IdOnboarding))?.OnboardingMode);

            Assert.NotNull(await store.ConsumeStateAsync(standard.StateHash, baseA, standard.UsuarioIniciador, now));
            await store.MarkAuthorizedAsync(standard.IdOnboarding, reference, string.Empty);
            var authorized = await store.GetAsync(standard.IdOnboarding);
            Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Authorized, authorized?.Status);
            Assert.Equal("AUTHORIZED", authorized?.CurrentStep);
            Assert.Equal(reference, authorized?.TokenReference);

            await Assert.ThrowsAsync<ArgumentException>(() => store.MarkAuthorizedAsync(coexistence.IdOnboarding, "", string.Empty));
            Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Started, (await store.GetAsync(coexistence.IdOnboarding))?.Status);
        }
        finally
        {
            await CleanupOnboardingsAsync([standard.IdOnboarding, coexistence.IdOnboarding]);
        }
    }

    [SqlIntegrationFact]
    public async Task StartAsync_RoundTripsTheAuthoritativeModeFromSql()
    {
        var store = new WhatsAppEmbeddedSignupStore(Configuration);
        var (baseA, _) = await GetTwoBaseIdsAsync();
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new UnusedMetaOAuthClient(),
            new UnusedCredentialVault(),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowedBaseIds = [baseA], OnboardingExpirationMinutes = 30 }));
        WhatsAppEmbeddedStartResult? standard = null;
        WhatsAppEmbeddedStartResult? coexistence = null;
        try
        {
            standard = await orchestrator.StartAsync(new(baseA, "TEST", "integration-test", WhatsAppEmbeddedOnboardingMode.Standard));
            coexistence = await orchestrator.StartAsync(new(baseA, "TEST", "integration-test", WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence));

            Assert.Equal(WhatsAppEmbeddedOnboardingMode.Standard, standard.OnboardingMode);
            Assert.Equal(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, coexistence.OnboardingMode);
            Assert.Equal(WhatsAppEmbeddedOnboardingMode.Standard, (await store.GetAsync(standard.IdOnboarding))?.OnboardingMode);
            Assert.Equal(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, (await store.GetAsync(coexistence.IdOnboarding))?.OnboardingMode);
        }
        finally
        {
            await CleanupOnboardingsAsync(new[] { standard?.IdOnboarding, coexistence?.IdOnboarding }.OfType<Guid>());
        }
    }

    [SqlIntegrationFact]
    public async Task UiChoiceMapping_RoundTripsDeterministicallyThroughStartAsyncAndSql()
    {
        var store = new WhatsAppEmbeddedSignupStore(Configuration);
        var (baseA, _) = await GetTwoBaseIdsAsync();
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new UnusedMetaOAuthClient(),
            new UnusedCredentialVault(),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowedBaseIds = [baseA], OnboardingExpirationMinutes = 30 }));
        var created = new List<Guid>();

        async Task AssertRoundTripAsync(WhatsAppConnectionChoice choice, WhatsAppEmbeddedOnboardingMode expectedMode)
        {
            var selection = new WhatsAppConnectionChoiceSelection();
            selection.Begin();
            selection.Select(choice);
            var immutableChoice = selection.Consume();
            var mappedMode = WhatsAppConnectionChoiceMapper.ToOnboardingMode(immutableChoice);
            Assert.Equal(expectedMode, mappedMode);

            var correlationId = Guid.NewGuid().ToString("N");
            var start = await orchestrator.StartAsync(new(baseA, "TEST", "ui-mapping-integration", mappedMode, correlationId));
            created.Add(start.IdOnboarding);
            var persisted = await store.GetAsync(start.IdOnboarding);
            Assert.Equal(expectedMode, start.OnboardingMode);
            Assert.Equal(expectedMode, persisted?.OnboardingMode);
            Assert.Equal(correlationId, persisted?.CorrelationId);
        }

        try
        {
            await AssertRoundTripAsync(WhatsAppConnectionChoice.NewWhatsApp, WhatsAppEmbeddedOnboardingMode.Standard);
            await AssertRoundTripAsync(WhatsAppConnectionChoice.ExistingWhatsAppBusiness, WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

            var selection = new WhatsAppConnectionChoiceSelection();
            selection.Begin();
            selection.Select(WhatsAppConnectionChoice.ExistingWhatsAppBusiness);
            selection.Clear();
            selection.Begin();
            selection.Select(WhatsAppConnectionChoice.NewWhatsApp);
            await AssertRoundTripAsync(selection.Consume(), WhatsAppEmbeddedOnboardingMode.Standard);

            selection.Begin();
            selection.Select(WhatsAppConnectionChoice.NewWhatsApp);
            selection.Clear();
            selection.Begin();
            selection.Select(WhatsAppConnectionChoice.ExistingWhatsAppBusiness);
            await AssertRoundTripAsync(selection.Consume(), WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);
        }
        finally
        {
            await CleanupOnboardingsAsync(created);
        }
    }

    [SqlIntegrationFact]
    public async Task Claiming_ProvidesSingleLeaseRecoveryAndExcludesTerminalStates()
    {
        var store = new WhatsAppEmbeddedSignupStore(Configuration);
        var (baseA, _) = await GetTwoBaseIdsAsync();
        // El store reclama globalmente. Un reloj futuro mantiene fuera del lease a onboardings
        // reales de prueba supervisada sin modificar ni limpiar su evidencia.
        var now = DateTime.UtcNow.AddDays(7);
        var claimable = NewOnboarding(baseA, WhatsAppEmbeddedOnboardingStatus.Authorized, now.AddHours(1));
        claimable.NextAttemptUtc = now.AddSeconds(-1);
        var terminal = new[] { WhatsAppEmbeddedOnboardingStatus.Ready, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedFinal }
            .Select(status => NewOnboarding(baseA, status, now.AddHours(1))).ToArray();
        try
        {
            await store.CreateAsync(claimable);
            var claims = await Task.WhenAll(
                store.ClaimNextAsync("worker-a", now, now.AddMinutes(1)),
                store.ClaimNextAsync("worker-b", now, now.AddMinutes(1)));
            Assert.Single(claims, x => x?.IdOnboarding == claimable.IdOnboarding);
            Assert.Single(claims, x => x is null);

            var recovered = await store.ClaimNextAsync("worker-c", now.AddMinutes(2), now.AddMinutes(3));
            Assert.Equal(claimable.IdOnboarding, recovered?.IdOnboarding);
            await store.ReleaseClaimAsync(claimable.IdOnboarding, "worker-c", now.AddHours(2));
            Assert.Null(await store.ClaimNextAsync("worker-d", now.AddMinutes(3), now.AddMinutes(4)));

            foreach (var onboarding in terminal)
                await store.CreateAsync(onboarding);
            Assert.Null(await store.ClaimNextAsync("worker-e", now.AddMinutes(3), now.AddMinutes(4)));
        }
        finally
        {
            await CleanupOnboardingsAsync([claimable.IdOnboarding, .. terminal.Select(x => x.IdOnboarding)]);
        }
    }

    [SqlIntegrationFact]
    public async Task Vault_RoundTripsAfterProviderRecreationAndStoresNoPlaintext()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), "alfacore-es-vault-tests", Guid.NewGuid().ToString("N"));
        var (baseA, _) = await GetTwoBaseIdsAsync();
        Directory.CreateDirectory(keyPath);
        var options = Options.Create(new WhatsAppEmbeddedSignupOptions { DataProtectionKeysPath = Path.GetFullPath(keyPath) });
        var context = new WhatsAppVaultSecretContext(baseA, null, "", "", "", "BUSINESS_TOKEN", null);
        var secret = $"secret-{Guid.NewGuid():N}";
        var createdReferences = new List<string>();
        try
        {
            var firstProvider = DataProtectionProvider.Create(new DirectoryInfo(keyPath), builder => builder.SetApplicationName("AlfaCore.WhatsAppEmbeddedSignup.Tests"));
            IWhatsAppCredentialVault firstVault = new WhatsAppSecureVault(Configuration, firstProvider, options);
            var reference = await firstVault.StoreAsync(context, secret.AsMemory());
            createdReferences.Add(reference.Value);

            var secondProvider = DataProtectionProvider.Create(new DirectoryInfo(keyPath), builder => builder.SetApplicationName("AlfaCore.WhatsAppEmbeddedSignup.Tests"));
            IWhatsAppCredentialVault secondVault = new WhatsAppSecureVault(Configuration, secondProvider, options);
            Assert.Equal(secret, (await secondVault.GetAsync(reference)).ToString());

            await using var cn = new SqlConnection(ConnectionString);
            var persisted = await cn.QuerySingleAsync<string>("SELECT ProtectedValue FROM dbo.WhatsAppSecureVault WHERE SecretReference=@Reference", new { Reference = reference.Value });
            Assert.DoesNotContain(secret, persisted, StringComparison.Ordinal);
            await secondVault.RemoveAsync(reference);

            var pinContext = context with { Purpose = "PHONE_REGISTRATION_PIN", PhoneNumberId = "999940000000000001" };
            IWhatsAppPhonePinVault pinVault = new WhatsAppSecureVault(Configuration, secondProvider, options);
            var pinReference = await pinVault.StoreAsync(pinContext, "123456".AsMemory());
            createdReferences.Add(pinReference.Value);
            var thirdProvider = DataProtectionProvider.Create(new DirectoryInfo(keyPath), builder => builder.SetApplicationName("AlfaCore.WhatsAppEmbeddedSignup.Tests"));
            IWhatsAppPhonePinVault recreatedPinVault = new WhatsAppSecureVault(Configuration, thirdProvider, options);
            Assert.Equal("123456", (await recreatedPinVault.GetAsync(pinReference)).ToString());
            var protectedPin = await cn.QuerySingleAsync<string>("SELECT ProtectedValue FROM dbo.WhatsAppSecureVault WHERE SecretReference=@Reference", new { Reference = pinReference.Value });
            Assert.DoesNotContain("123456", protectedPin, StringComparison.Ordinal);
            await recreatedPinVault.RemoveAsync(pinReference);
        }
        finally
        {
            await CleanupVaultAsync(createdReferences);
            Directory.Delete(keyPath, recursive: true);
        }
    }

    private static WhatsAppEmbeddedOnboardingDto NewOnboarding(int idBase, WhatsAppEmbeddedOnboardingStatus status, DateTime expiration)
        => new()
        {
            IdOnboarding = Guid.NewGuid(), IdBase = idBase, IdCliente = "TEST", UsuarioIniciador = "integration-test",
            CorrelationId = Guid.NewGuid().ToString("N"), StateHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            Status = status, CurrentStep = status.ToString(), StartedAtUtc = DateTime.UtcNow, ModifiedAtUtc = DateTime.UtcNow, ExpiresAtUtc = expiration
        };

    private static async Task CleanupOnboardingsAsync(IEnumerable<Guid> ids)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync("DELETE FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdOnboarding IN @Ids AND IdCliente='TEST'", new { Ids = ids.ToArray() });
    }

    private static async Task CleanupOwnershipAsync(IEnumerable<string> phoneIds, IEnumerable<string> wabaIds)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync("DELETE FROM dbo.WhatsAppPhoneOwnership WHERE PhoneNumberId IN @PhoneIds", new { PhoneIds = phoneIds.ToArray() });
        await cn.ExecuteAsync("DELETE FROM dbo.WhatsAppWabaOwnership WHERE WabaId IN @WabaIds", new { WabaIds = wabaIds.ToArray() });
    }

    private static async Task CleanupVaultAsync(IEnumerable<string> references)
    {
        var values = references.ToArray();
        if (values.Length == 0) return;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync("DELETE FROM dbo.WhatsAppSecureVault WHERE SecretReference IN @References", new { References = values });
    }

    private static async Task<(int BaseA, int BaseB)> GetTwoBaseIdsAsync()
    {
        await using var cn = new SqlConnection(ConnectionString);
        var ids = (await cn.QueryAsync<int>("SELECT TOP (2) id FROM dbo.bases ORDER BY id")).ToArray();
        if (ids.Length < 2) throw new InvalidOperationException("La ALFA_CENTRAL de test debe contener al menos dos bases fixture.");
        return (ids[0], ids[1]);
    }

    private sealed class UnusedMetaOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class UnusedCredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.CompletedTask;
    }
}
