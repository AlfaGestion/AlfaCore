using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppEmbeddedOperationalImportServiceTests
{
    [Fact]
    public async Task Complete_ImportsAllRegisteredPhonesAcrossAuthorizedWabas()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84);
        context.Management.Businesses.Add(new("business-1", "Business 1"));
        context.Management.Businesses.Add(new("business-2", "Business 2"));
        context.Management.WabasByBusiness["business-1"] = [new("waba-1", "business-1", "WABA 1")];
        context.Management.WabasByBusiness["business-2"] = [new("waba-2", "business-2", "WABA 2")];
        context.Management.PhonesByWaba["waba-1"] =
        [
            Phone("phone-1", "waba-1", "+54 11 1", "Ventas"),
            Phone("phone-2", "waba-1", "+54 11 2", "Soporte")
        ];
        context.Management.PhonesByWaba["waba-2"] =
        [
            Phone("phone-3", "waba-2", "+54 11 3", "Demo")
        ];

        var result = await context.Service.CompleteAsync(idOnboarding);

        Assert.Equal(3, result.Count);
        Assert.Equal(["phone-1", "phone-2", "phone-3"], context.Config.Numeros.Select(x => x.PhoneNumberId).ToArray());
        Assert.Equal(["waba-1", "waba-2"], context.Ownership.ReservedWabas.Select(x => x.WabaId).ToArray());
        Assert.Equal(["phone-1", "phone-2", "phone-3"], context.Ownership.ReservedPhones.Select(x => x.PhoneNumberId).ToArray());
        Assert.True(context.Store.MarkedReady);
    }

    [Fact]
    public async Task Complete_ReusesSamePhoneNumberIdAndPreservesUsers()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84);
        context.Management.Businesses.Add(new("business-1", "Business 1"));
        context.Management.WabasByBusiness["business-1"] = [new("waba-1", "business-1", "WABA 1")];
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Ventas")];
        context.Config.Numeros.Add(new ConversacionWhatsAppNumeroDto
        {
            IdNumero = 25,
            PhoneNumberId = "phone-1",
            Nombre = "Nombre anterior",
            Activo = false,
            Usuarios = ["eve"]
        });

        var result = await context.Service.CompleteAsync(idOnboarding);

        Assert.Equal(1, result.Count);
        Assert.Single(context.Config.Numeros);
        Assert.Equal(25, context.Config.Numeros[0].IdNumero);
        Assert.Equal("Ventas", context.Config.Numeros[0].Nombre);
        Assert.True(context.Config.Numeros[0].Activo);
        Assert.Equal(["eve"], context.Config.Numeros[0].Usuarios);
    }

    [Fact]
    public async Task Complete_ImportsBusinessAppCoexistenceWithoutCloudRegistration()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84, WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);
        context.Management.Businesses.Add(new("business-1", "Business 1"));
        context.Management.WabasByBusiness["business-1"] = [new("waba-1", "business-1", "WABA 1")];
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Coexistence")];

        var result = await context.Service.CompleteAsync(idOnboarding);

        Assert.Equal(1, result.Count);
        Assert.True(context.Store.MarkedReady);
        Assert.Single(context.Config.Numeros);
    }

    [Fact]
    public async Task Complete_UsesPersistedWabaBeforeBroadBusinessDiscovery()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84, WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);
        context.Vault.Context = new WhatsAppVaultSecretContext(84, idOnboarding, "business-1", "waba-1", "phone-1", "META_EMBEDDED_SIGNUP_BUSINESS_AUTHORIZATION", null);
        context.Management.ThrowOnDiscoverBusinesses = true;
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Coexistence")];

        var result = await context.Service.CompleteAsync(idOnboarding);

        Assert.Equal(1, result.Count);
        Assert.Equal(0, context.Management.DiscoverBusinessCalls);
        Assert.True(context.Store.MarkedReady);
    }

    [Fact]
    public async Task Complete_ImportsExistingCoexistenceAtLegacyReadyForImportApprovalStep()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84,
            WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence,
            currentStep: "READY_FOR_IMPORT_APPROVAL");
        context.Management.Businesses.Add(new("business-1", "Business 1"));
        context.Management.WabasByBusiness["business-1"] = [new("waba-1", "business-1", "WABA 1")];
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Coexistence")];

        var result = await context.Service.CompleteAsync(idOnboarding);

        Assert.Equal(1, result.Count);
        Assert.True(context.Store.MarkedReady);
        Assert.Single(context.Config.Numeros);
    }

    [Fact]
    public async Task PendingConnections_ProjectsAuthorizedCoexistenceBeforeOperationalImport()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84, WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence,
            WhatsAppEmbeddedOnboardingStatus.Authorized);
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Coexistence")];
        context.Vault.Context = new WhatsAppVaultSecretContext(84, idOnboarding, "business-1", "waba-1", "phone-1", "META_EMBEDDED_SIGNUP_BUSINESS_AUTHORIZATION", null);

        var pending = await context.Service.GetPendingConnectionsAsync();

        var item = Assert.Single(pending);
        Assert.Equal("Coexistence", item.Nombre);
        Assert.Equal("+54 11 1", item.DisplayPhoneNumber);
        Assert.True(item.IsInProgress);
    }

    [Fact]
    public async Task PendingConnections_ReusesDiscoveredMetadataDuringPolling()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84, WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence,
            WhatsAppEmbeddedOnboardingStatus.Authorized);
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Coexistence")];
        context.Vault.Context = new WhatsAppVaultSecretContext(84, idOnboarding, "business-1", "waba-1", "phone-1", "META_EMBEDDED_SIGNUP_BUSINESS_AUTHORIZATION", null);

        await context.Service.GetPendingConnectionsAsync();
        await context.Service.GetPendingConnectionsAsync();

        Assert.Equal(1, context.Management.DiscoverPhoneCalls);
    }

    [Fact]
    public async Task Complete_CoexistenceReady_TriggersInitialSyncsOnceWithOnBizAppPhones()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84, WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);
        context.Management.Businesses.Add(new("business-1", "Business 1"));
        context.Management.WabasByBusiness["business-1"] = [new("waba-1", "business-1", "WABA 1")];
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Coexistence", isOnBizApp: true)];

        await context.Service.CompleteAsync(idOnboarding);

        var call = Assert.Single(context.CoexistenceTrigger.Calls);
        Assert.Equal(WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence, call.Onboarding.OnboardingMode);
        var phone = Assert.Single(call.Phones);
        Assert.Equal("phone-1", phone.PhoneNumberId);
        Assert.True(phone.IsOnBizApp);
    }

    [Fact]
    public async Task Complete_StandardReady_NeverTriggersCoexistenceSync()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84, WhatsAppEmbeddedOnboardingMode.Standard);
        context.Management.Businesses.Add(new("business-1", "Business 1"));
        context.Management.WabasByBusiness["business-1"] = [new("waba-1", "business-1", "WABA 1")];
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Ventas")];

        await context.Service.CompleteAsync(idOnboarding);

        Assert.Empty(context.CoexistenceTrigger.Calls);
    }

    [Fact]
    public async Task Complete_BlocksPhoneOwnedByAnotherBase()
    {
        var idOnboarding = Guid.NewGuid();
        var context = CreateContext(idOnboarding, activeBaseId: 84);
        context.Management.Businesses.Add(new("business-1", "Business 1"));
        context.Management.WabasByBusiness["business-1"] = [new("waba-1", "business-1", "WABA 1")];
        context.Management.PhonesByWaba["waba-1"] = [Phone("phone-1", "waba-1", "+54 11 1", "Ventas")];
        context.Ownership.PhoneOwners["phone-1"] = 106;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Service.CompleteAsync(idOnboarding));
        Assert.Empty(context.Config.Numeros);
        Assert.False(context.Store.MarkedReady);
    }

    private static TestContext CreateContext(
        Guid idOnboarding,
        int activeBaseId,
        WhatsAppEmbeddedOnboardingMode mode = WhatsAppEmbeddedOnboardingMode.Standard,
        WhatsAppEmbeddedOnboardingStatus status = WhatsAppEmbeddedOnboardingStatus.Importing,
        string currentStep = "READY_FOR_OPERATIONAL_UPSERT")
    {
        var store = new MemoryOnboardingStore(idOnboarding, activeBaseId, mode, status, currentStep);
        var vault = new MemoryCredentialVault(new WhatsAppVaultSecretContext(
            activeBaseId,
            idOnboarding,
            string.Empty,
            string.Empty,
            string.Empty,
            "META_EMBEDDED_SIGNUP_BUSINESS_AUTHORIZATION",
            null));
        var management = new MemoryMetaManagementClient();
        var ownership = new MemoryOwnershipStore();
        var config = new MemoryConversacionesConfigService();
        var session = new MemorySessionService(activeBaseId);
        var coexistenceTrigger = new RecordingCoexistenceSyncTrigger();
        var service = new WhatsAppEmbeddedOperationalImportService(
            store,
            vault,
            management,
            ownership,
            config,
            session,
            coexistenceTrigger,
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowedBaseIds = [84] }),
            NullLogger<WhatsAppEmbeddedOperationalImportService>.Instance);

        return new(service, store, vault, management, ownership, config, session, coexistenceTrigger);
    }

    private static MetaPhoneAsset Phone(string id, string wabaId, string display, string name, bool isOnBizApp = false)
        => new(id, wabaId, display, name, "CONNECTED", "GREEN", MetaPhoneRegistrationStatus.Registered, isOnBizApp);

    private sealed record TestContext(
        WhatsAppEmbeddedOperationalImportService Service,
        MemoryOnboardingStore Store,
        MemoryCredentialVault Vault,
        MemoryMetaManagementClient Management,
        MemoryOwnershipStore Ownership,
        MemoryConversacionesConfigService Config,
        MemorySessionService Session,
        RecordingCoexistenceSyncTrigger CoexistenceTrigger);

    private sealed class RecordingCoexistenceSyncTrigger : IWhatsAppCoexistenceSyncTrigger
    {
        public List<(WhatsAppEmbeddedOnboardingDto Onboarding, IReadOnlyList<WhatsAppCoexistencePhoneCandidate> Phones)> Calls { get; } = [];

        public Task TriggerInitialSyncsAsync(
            WhatsAppEmbeddedOnboardingDto onboarding,
            IReadOnlyList<WhatsAppCoexistencePhoneCandidate> phones,
            WhatsAppCredentialReference tokenReference,
            CancellationToken ct = default)
        {
            Calls.Add((onboarding, phones));
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryOnboardingStore(
        Guid idOnboarding,
        int idBase,
        WhatsAppEmbeddedOnboardingMode mode,
        WhatsAppEmbeddedOnboardingStatus status,
        string currentStep) : IWhatsAppEmbeddedSignupStore
    {
        private readonly WhatsAppEmbeddedOnboardingDto _item = new()
        {
            IdOnboarding = idOnboarding,
            IdBase = idBase,
            IdCliente = "ALFANET",
            UsuarioIniciador = "Eve",
            Status = status,
            OnboardingMode = mode,
            CurrentStep = currentStep,
            TokenReference = "token-ref",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };

        public bool MarkedReady { get; private set; }
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(id == _item.IdOnboarding ? _item : null);
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int requestedBaseId, CancellationToken ct = default) => Task.FromResult(requestedBaseId == _item.IdBase ? _item : null);
        public Task<IReadOnlyList<WhatsAppEmbeddedOnboardingDto>> GetPendingForBaseAsync(int requestedBaseId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WhatsAppEmbeddedOnboardingDto>>(requestedBaseId == _item.IdBase ? [_item] : []);
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int requestedBaseId, string usuario, DateTime nowUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Guid id, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkAuthorizedAsync(Guid id, string tokenReference, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkActionRequiredAsync(Guid id, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkRetryableFailureAsync(Guid id, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkFinalFailureAsync(Guid id, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid id, CancellationToken ct = default) { MarkedReady = id == _item.IdOnboarding; return Task.CompletedTask; }
        public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid id, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class MemoryCredentialVault(WhatsAppVaultSecretContext context) : IWhatsAppCredentialVault
    {
        public WhatsAppVaultSecretContext Context { get; set; } = context;
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext vaultContext, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>("token".AsMemory());
        public Task<WhatsAppVaultSecretContext?> GetContextAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.FromResult<WhatsAppVaultSecretContext?>(Context);
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class MemoryMetaManagementClient : IMetaWhatsAppManagementClient
    {
        public List<MetaAuthorizedBusiness> Businesses { get; } = [];
        public Dictionary<string, IReadOnlyList<MetaWabaAsset>> WabasByBusiness { get; } = [];
        public Dictionary<string, IReadOnlyList<MetaPhoneAsset>> PhonesByWaba { get; } = [];
        public int DiscoverPhoneCalls { get; private set; }
        public int DiscoverBusinessCalls { get; private set; }
        public bool ThrowOnDiscoverBusinesses { get; set; }

        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
        {
            DiscoverBusinessCalls++;
            if (ThrowOnDiscoverBusinesses)
                throw new InvalidOperationException("Broad business discovery must not run when the WABA is persisted.");
            return Task.FromResult<IReadOnlyList<MetaAuthorizedBusiness>>(Businesses);
        }
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Task.FromResult(WabasByBusiness.TryGetValue(businessId, out var value) ? value : []);
        public Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
        {
            DiscoverPhoneCalls++;
            return Task.FromResult(PhonesByWaba.TryGetValue(wabaId, out var value) ? value : []);
        }
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pinReference, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class MemoryOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        public Dictionary<string, int> WabaOwners { get; } = [];
        public Dictionary<string, int> PhoneOwners { get; } = [];
        public List<(string WabaId, int IdBase)> ReservedWabas { get; } = [];
        public List<(string PhoneNumberId, string WabaId, int IdBase)> ReservedPhones { get; } = [];

        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default)
        {
            var result = WhatsAppAssetOwnershipPolicy.Evaluate(WabaOwners.TryGetValue(wabaId, out var owner) ? owner : null, idBase);
            if (result != WhatsAppAssetOwnershipResult.Conflict)
            {
                WabaOwners[wabaId] = idBase;
                ReservedWabas.Add((wabaId, idBase));
            }
            return Task.FromResult(new WhatsAppAssetOwnershipDecision(result, WabaOwners.GetValueOrDefault(wabaId), wabaId));
        }

        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default)
        {
            var result = WhatsAppAssetOwnershipPolicy.Evaluate(PhoneOwners.TryGetValue(phoneNumberId, out var owner) ? owner : null, idBase);
            if (result != WhatsAppAssetOwnershipResult.Conflict)
            {
                PhoneOwners[phoneNumberId] = idBase;
                ReservedPhones.Add((phoneNumberId, wabaId, idBase));
            }
            return Task.FromResult(new WhatsAppAssetOwnershipDecision(result, PhoneOwners.GetValueOrDefault(phoneNumberId), phoneNumberId));
        }

        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class MemoryConversacionesConfigService : IConversacionesConfigService
    {
        private int _nextId = 1;
        public List<ConversacionWhatsAppNumeroDto> Numeros { get; } = [];

        public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConversacionWhatsAppNumeroDto>>(Numeros);

        public Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default)
        {
            var existing = Numeros.SingleOrDefault(item => string.Equals(item.PhoneNumberId, numero.PhoneNumberId, StringComparison.Ordinal));
            if (existing is null)
            {
                numero.IdNumero = _nextId++;
                Numeros.Add(Clone(numero));
                return Task.CompletedTask;
            }

            existing.Nombre = numero.Nombre;
            existing.Activo = numero.Activo;
            if (numero.Usuarios.Count > 0)
                existing.Usuarios = [.. numero.Usuarios];
            return Task.CompletedTask;
        }

        public async Task<ConversacionWhatsAppNumeroDto> UpsertEmbeddedSignupWhatsAppNumeroForBaseAsync(
            int idBase,
            ConversacionWhatsAppNumeroDto numero,
            CancellationToken ct = default)
        {
            await SaveWhatsAppNumeroAsync(numero, ct);
            return Numeros.Single(item => string.Equals(item.PhoneNumberId, numero.PhoneNumberId, StringComparison.Ordinal));
        }

        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> GenerateWhatsAppWebPairingAsync(ConversacionWhatsAppWebPairingRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppConfigDto> ClearWhatsAppWebPairingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMercadoLibreTokensAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAlfaKnowledgeConfigForConnectionAsync(string connectionString, ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAlfaKnowledgeConnectionTestResultDto> TestAlfaKnowledgeConnectionAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAutomatizacionesConfigAsync(ConversacionAutomatizacionesConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SavePrioridadConfigAsync(ConversacionPrioridadConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroByInstanceNameAsync(string instanceName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveWhatsAppNumeroWebSessionAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveConversacionAdministradoresAsync(IReadOnlyList<string> usuarios, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConversacionesInboxPreferenceDto> GetInboxPreferenceAsync(string userName, string? sistema, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveInboxPreferenceAsync(string userName, string? sistema, ConversacionesInboxPreferenceDto preference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveReglaAsync(ConversacionReglaDto regla, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteReglaAsync(int idRegla, CancellationToken ct = default) => throw new NotSupportedException();

        private static ConversacionWhatsAppNumeroDto Clone(ConversacionWhatsAppNumeroDto source)
            => new()
            {
                IdNumero = source.IdNumero,
                PhoneNumberId = source.PhoneNumberId,
                Nombre = source.Nombre,
                Activo = source.Activo,
                Usuarios = [.. source.Usuarios]
            };
    }

    private sealed class MemorySessionService(int activeBaseId) : ISessionService
    {
        public string GetConnectionString() => string.Empty;
        public SessionDto? GetActiveSession() => new() { BaseId = activeBaseId, Nombre = "ALFANET" };
        public void SetWebhookOverride(SessionDto session) => throw new NotSupportedException();
        public void ClearWebhookOverride() => throw new NotSupportedException();
        public IReadOnlyList<SessionDto> GetAllSessions() => throw new NotSupportedException();
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => throw new NotSupportedException();
        public event Action? SessionChanged { add { } remove { } }
    }
}
