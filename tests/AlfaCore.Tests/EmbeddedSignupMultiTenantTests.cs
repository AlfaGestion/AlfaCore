using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Modelo multi-tenant: <c>Enabled</c> (switch global) + <c>AllowAllTenants</c>/<c>AllowedBaseIds</c>
/// (sólo iniciar onboardings) + ownership central (autoridad del runtime de un asset existente).
/// </summary>
public sealed class EmbeddedSignupMultiTenantTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static WhatsAppEmbeddedSignupOptions Runtime(bool enabled, bool allowAll = false, int[]? allowList = null)
        => new()
        {
            Enabled = enabled,
            AllowAllTenants = allowAll,
            AllowedBaseIds = allowList ?? [],
            GraphApiVersion = "v26.0",
            DataProtectionKeysPath = @"C:\AlfaCore\EmbeddedSignupKeys"
        };

    // ---- CanStartEmbeddedSignup: elegibilidad para INICIAR onboardings ---------------------

    [Fact]
    public void CanStart_DefaultsAreSafe_NothingAllowed()
    {
        var o = new WhatsAppEmbeddedSignupOptions();
        Assert.False(o.Enabled);
        Assert.False(o.AllowAllTenants);
        Assert.Empty(o.AllowedBaseIds);
        Assert.False(o.CanStartEmbeddedSignup(84));
        Assert.False(o.CanStartEmbeddedSignup(142));
    }

    [Fact]
    public void CanStart_AllowAllTenants_PermitsAnyBase()
    {
        var o = new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowAllTenants = true };
        Assert.True(o.CanStartEmbeddedSignup(142));
        Assert.True(o.CanStartEmbeddedSignup(84));
        Assert.False(o.CanStartEmbeddedSignup(0));
    }

    [Fact]
    public void CanStart_NoAllowAll_EmptyList_DisablesNewOnboardings_ButFeatureStaysEnabled()
    {
        var o = new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowAllTenants = false, AllowedBaseIds = [] };
        Assert.True(o.Enabled);
        Assert.False(o.CanStartEmbeddedSignup(84));
        Assert.False(o.CanStartEmbeddedSignup(142));
    }

    [Fact]
    public void CanStart_AllowList_RestrictsToListedBases()
    {
        var o = new WhatsAppEmbeddedSignupOptions { Enabled = true, AllowAllTenants = false, AllowedBaseIds = [84] };
        Assert.True(o.CanStartEmbeddedSignup(84));
        Assert.False(o.CanStartEmbeddedSignup(142));
    }

    [Fact]
    public void CanStart_Disabled_NeverPermits_EvenWithAllowAll()
    {
        var o = new WhatsAppEmbeddedSignupOptions { Enabled = false, AllowAllTenants = true, AllowedBaseIds = [84] };
        Assert.False(o.CanStartEmbeddedSignup(84));
    }

    // ---- Startup: lista vacía ya no es requisito -----------------------------------------

    [Fact]
    public void Startup_ValidWith_EnabledTrue_AllowAllFalse_EmptyList()
    {
        var o = new WhatsAppEmbeddedSignupOptions
        {
            Enabled = true,
            AllowAllTenants = false,
            AllowedBaseIds = [],
            WorkerEnabled = false,
            UseApplicationCentralConnection = true,
            AppSecret = "app-secret"
        };
        Assert.True(o.HasWebhookRuntimeConfiguration());
        Assert.True(o.IsValidStartupConfiguration());
    }

    [Fact]
    public void Startup_StillRequires_AppSecret()
    {
        var o = new WhatsAppEmbeddedSignupOptions
        {
            Enabled = true,
            AllowAllTenants = true,
            UseApplicationCentralConnection = true,
            AppSecret = ""
        };
        Assert.False(o.IsValidStartupConfiguration());
    }

    // ---- Runtime resolver: ownership es la autoridad, NUNCA fallback legacy para assets ES ----

    private static WhatsAppRuntimeCredentialResolver Resolver(WhatsAppPhoneOwnership? ownership, WhatsAppEmbeddedSignupOptions options)
        => new(new FootprintOwnershipStore(ownership), new StubVault(new("ref"), "vault-token"), Options.Create(options));

    private static ConversacionWhatsAppConfigDto Legacy()
        => new() { AccessToken = "legacy-token", PhoneNumberId = "legacy", BusinessAccountId = "legacy-waba", ApiVersion = "v22.0" };

    [Fact]
    public async Task OwnedEsAsset_FeatureDisabled_Throws_NeverLegacy()
    {
        var resolver = Resolver(new("9201", "9101", 84, DateTime.UtcNow), Runtime(enabled: false));
        await Assert.ThrowsAsync<WhatsAppEmbeddedVaultUnavailableException>(
            () => resolver.ResolveAsync(84, 1, "9201", Legacy()));
    }

    [Fact]
    public async Task OwnedEsAsset_BaseNotInAllowList_StillResolvesEsRuntime()
    {
        // Enabled=true, AllowAllTenants=false, AllowedBaseIds=[] => no se pueden iniciar onboardings
        // nuevos, pero un asset YA onboardeado (ownership IdBase=142) sigue siendo ES en runtime.
        var resolver = Resolver(new("9201", "9101", 142, DateTime.UtcNow), Runtime(enabled: true, allowAll: false, allowList: []));
        var result = await resolver.ResolveAsync(142, 1, "9201", Legacy());
        Assert.Equal(WhatsAppRuntimeCredentialOrigin.EmbeddedSignup, result.Origin);
        Assert.Equal("vault-token", result.AccessToken);
    }

    [Fact]
    public async Task NoOwnership_ResolvesLegacy()
    {
        var resolver = Resolver(ownership: null, Runtime(enabled: true, allowAll: true));
        var result = await resolver.ResolveAsync(142, 1, "9201", Legacy());
        Assert.Equal(WhatsAppRuntimeCredentialOrigin.Legacy, result.Origin);
        Assert.Equal("legacy-token", result.AccessToken);
    }

    [Fact]
    public async Task OwnedByAnotherBase_Throws_CrossTenant()
    {
        var resolver = Resolver(new("9201", "9101", 106, DateTime.UtcNow), Runtime(enabled: true, allowAll: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(84, 1, "9201", Legacy()));
    }

    // ---- Guard: footprint decide fail-closed vs legacy passthrough ------------------------

    [Fact]
    public async Task Guard_FootprintBase_UnknownPhone_FailsClosed()
    {
        var guard = new WhatsAppWebhookTenantGuard(
            new FootprintOwnershipStore(null, hasFootprint: true), Options.Create(Runtime(enabled: true, allowAll: true)));
        await Assert.ThrowsAsync<WhatsAppWebhookPhoneOwnershipMissingException>(() => guard.ValidateAsync(142, ["7777"]));
    }

    [Fact]
    public async Task Guard_NoFootprintBase_UnknownPhone_PassesThrough()
    {
        var guard = new WhatsAppWebhookTenantGuard(
            new FootprintOwnershipStore(null, hasFootprint: false), Options.Create(Runtime(enabled: true, allowAll: true)));
        await guard.ValidateAsync(142, ["7777"]);   // no throw => legacy passthrough
    }

    // ---- Worker: sin lista estática cuando AllowAllTenants ------------------------------

    [Fact]
    public void Worker_SelectsClaimStrategyByAllowAllTenants()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "src", "AlfaCore", "Services", "WhatsAppEmbeddedSignupHostedService.cs"));
        Assert.Contains("_options.AllowAllTenants", src, StringComparison.Ordinal);
        Assert.Contains("store.ClaimNextAsync(_workerId", src, StringComparison.Ordinal);
        Assert.Contains("_options.AllowedBaseIds.Length > 0", src, StringComparison.Ordinal);
        Assert.Contains("ClaimNextForBasesAsync(_workerId, _options.AllowedBaseIds", src, StringComparison.Ordinal);
    }

    // ---- assets existentes siguen visibles aunque CanStart=false ------------------------

    [Fact]
    public void ExistingAssetsRemainManageable_WhenNewConnectionsDisabled()
    {
        // El backend gatea la carga/gestión de assets por Enabled (no por CanStart): una base con
        // footprint ES puede ver sus números aunque no pueda iniciar nuevas conexiones.
        var razor = File.ReadAllText(Path.Combine(RepoRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));
        Assert.Contains("if (!EmbeddedSignupOptions.Value.Enabled)", razor, StringComparison.Ordinal);
        Assert.Contains("HasEmbeddedSignupPresenceForActiveBase", razor, StringComparison.Ordinal);
        Assert.Contains("CanStartEmbeddedSignupForActiveBase", razor, StringComparison.Ordinal);

        var orchestrator = File.ReadAllText(Path.Combine(RepoRoot, "src", "AlfaCore", "Services", "WhatsAppEmbeddedSignupOrchestrator.cs"));
        Assert.Contains("EnsureCanStart(request.IdBase);", orchestrator, StringComparison.Ordinal);      // sólo StartAsync
        Assert.Contains("private void EnsureFeatureEnabled()", orchestrator, StringComparison.Ordinal);   // resto opera con feature on
    }

    private sealed class FootprintOwnershipStore(WhatsAppPhoneOwnership? ownership, bool hasFootprint = false) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult(ownership);
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<bool> HasEmbeddedSignupFootprintAsync(int idBase, CancellationToken ct = default) => Task.FromResult(hasFootprint || ownership is not null);
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string a, int b, string c, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubVault(WhatsAppCredentialReference? reference, string secret) : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference?> FindActiveCredentialAsync(int a, string b, string c, CancellationToken ct = default) => Task.FromResult(reference);
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>(secret.AsMemory());
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AlfaCore.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
