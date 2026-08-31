using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppEmbeddedSignupAuthorizationTests
{
    [Fact] public async Task DisabledFeature_RejectsStart() => await Assert.ThrowsAsync<InvalidOperationException>(() => Create(enabled: false).Orchestrator.StartAsync(new(106, "TEST", "Eve", WhatsAppEmbeddedOnboardingMode.Standard)));
    [Fact] public async Task BaseOutsideAllowlist_RejectsStartBeforeCreatingOnboarding()
    {
        var context = Create(allowedBaseIds: [84]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Orchestrator.StartAsync(new(106, "TEST", "Eve", WhatsAppEmbeddedOnboardingMode.Standard)));
        Assert.Null(context.Store.Item);
        Assert.Equal(0, context.Meta.Calls);
    }
    [Fact] public async Task MissingOnboarding_IsRejected() => await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create().Orchestrator.HandleAuthorizationCallbackAsync(Callback(Guid.NewGuid(), 106, "state", "Eve")));
    [Fact] public async Task EmptyCode_IsRejected() { var c = CreateWithOnboarding(); await Assert.ThrowsAsync<ArgumentException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 106, c.State!, "Eve", ""))); }
    [Fact] public async Task WrongUser_IsRejected() { var c = CreateWithOnboarding(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 106, c.State!, "Otro"))); }
    [Fact] public async Task WrongBase_IsRejected() { var c = CreateWithOnboarding(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 205, c.State!, "Eve"))); }
    [Fact] public async Task WrongState_IsRejected() { var c = CreateWithOnboarding(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 106, "otro-state", "Eve"))); }
    [Fact] public async Task ExpiredState_IsRejected() { var c = CreateWithOnboarding(expired: true); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 106, c.State!, "Eve"))); }
    [Fact] public async Task Cancel_IsNotTechnicalFailureAndConsumesState() { var c = CreateWithOnboarding(); await c.Orchestrator.HandleCancellationAsync(c.Item!.IdOnboarding, 106, c.State!, "Eve"); Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Cancelled, c.Item.Status); Assert.NotNull(c.Item.StateConsumedAtUtc); Assert.Equal(0, c.Meta.Calls); }

    [Fact]
    public async Task SuccessfulExchange_StoresOnlyReferenceAndMarksAuthorized()
    {
        var c = CreateWithOnboarding();
        await c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 106, c.State!, "Eve"));
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Authorized, c.Item.Status);
        Assert.Equal("vault-reference", c.Item.TokenReference);
        Assert.Equal(1, c.Meta.Calls);
        Assert.DoesNotContain("authorization-code", c.Item.GetType().GetProperties().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReusedStateAndDoubleCallback_ProduceOnlyOneExchange()
    {
        var c = CreateWithOnboarding(); var callback = Callback(c.Item!.IdOnboarding, 106, c.State!, "Eve");
        await c.Orchestrator.HandleAuthorizationCallbackAsync(callback);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(callback));
        Assert.Equal(1, c.Meta.Calls);
    }

    [Fact]
    public async Task ExchangeFailure_DoesNotAuthorizeOrPersistReference()
    {
        var c = CreateWithOnboarding(); c.Meta.Throw = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 106, c.State!, "Eve")));
        Assert.Equal(WhatsAppEmbeddedOnboardingStatus.Started, c.Item!.Status);
        Assert.Empty(c.Item.TokenReference);
    }

    [Fact]
    public async Task StoreFailure_RevokesVaultReferenceAndDoesNotAuthorize()
    {
        var c = CreateWithOnboarding(); c.Store.FailAuthorization = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Orchestrator.HandleAuthorizationCallbackAsync(Callback(c.Item!.IdOnboarding, 106, c.State!, "Eve")));
        Assert.True(c.Vault.Removed);
        Assert.NotEqual(WhatsAppEmbeddedOnboardingStatus.Authorized, c.Item!.Status);
    }

    [Fact]
    public async Task RealMetaClient_SimulatedExchangeSendsTokenDirectlyToVault()
    {
        var vault = new CapturingVault();
        var handler = new StubHandler(HttpStatusCode.OK, "{\"access_token\":\"fake-token-value\",\"expires_in\":3600}");
        var client = new MetaOAuthClient(new SingleClientFactory(new HttpClient(handler)), vault, Options.Create(new WhatsAppEmbeddedSignupOptions { AppId = "999", AppSecret = "fake-app-secret", GraphApiVersion = "v26.0" }));
        var result = await client.ExchangeCodeAsync("fake-code", new(106, Guid.NewGuid(), "", "", "", "TEST", null));
        Assert.Equal("fake-token-value", vault.StoredSecret);
        Assert.Equal("captured-reference", result.TokenReference.Value);
        Assert.Equal(1, vault.StoreCalls);
    }

    [Fact]
    public async Task RealMetaClient_FailedExchangeDoesNotWriteVault()
    {
        var vault = new CapturingVault();
        var client = new MetaOAuthClient(new SingleClientFactory(new HttpClient(new StubHandler(HttpStatusCode.BadRequest, "{}"))), vault, Options.Create(new WhatsAppEmbeddedSignupOptions { AppId = "999", AppSecret = "fake-app-secret" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExchangeCodeAsync("fake-code", new(106, null, "", "", "", "TEST", null)));
        Assert.Null(vault.StoredSecret);
    }

    [Fact]
    public void BrowserModule_UsesExactOriginsAndNoBrowserPersistence()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = File.ReadAllText(Path.Combine(directory!.FullName, "src", "AlfaCore", "wwwroot", "js", "whatsappEmbeddedSignup.js"));
        Assert.Contains("https://www.facebook.com", script, StringComparison.Ordinal);
        Assert.Contains("https://web.facebook.com", script, StringComparison.Ordinal);
        Assert.DoesNotContain("endsWith(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("includes(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
    }

    private static WhatsAppEmbeddedAuthorizationCallback Callback(Guid id, int idBase, string state, string user, string code = "fake-authorization-code") => new(id, idBase, state, code, user);

    private static Context Create(bool enabled = true, int[]? allowedBaseIds = null)
    {
        var store = new MemoryStore(); var meta = new FakeMetaClient(); var vault = new FakeVault(); var protector = new WhatsAppEmbeddedSignupStateProtector();
        return new(new(store, protector, meta, vault, Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = enabled, AllowedBaseIds = allowedBaseIds ?? [106], OnboardingExpirationMinutes = 30 })), store, meta, vault, protector);
    }

    private static Context CreateWithOnboarding(bool expired = false)
    {
        var context = Create(); var pair = context.Protector.Create();
        context.State = pair.State;
        context.Item = new() { IdOnboarding = Guid.NewGuid(), IdBase = 106, IdCliente = "TEST", UsuarioIniciador = "Eve", StateHash = pair.Hash, Status = WhatsAppEmbeddedOnboardingStatus.Started, ExpiresAtUtc = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10) };
        context.Store.Item = context.Item;
        return context;
    }

    private sealed class Context(WhatsAppEmbeddedSignupOrchestrator orchestrator, MemoryStore store, FakeMetaClient meta, FakeVault vault, WhatsAppEmbeddedSignupStateProtector protector)
    {
        public WhatsAppEmbeddedSignupOrchestrator Orchestrator { get; } = orchestrator; public MemoryStore Store { get; } = store; public FakeMetaClient Meta { get; } = meta; public FakeVault Vault { get; } = vault; public WhatsAppEmbeddedSignupStateProtector Protector { get; } = protector; public WhatsAppEmbeddedOnboardingDto? Item { get; set; } public string? State { get; set; }
    }

    private sealed class FakeMetaClient : IMetaOAuthClient
    {
        public int Calls { get; private set; } public bool Throw { get; set; }
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default) { Calls++; if (Throw) throw new InvalidOperationException("safe failure"); return Task.FromResult(new MetaTokenExchangeResult(new("vault-reference"), null)); }
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeVault : IWhatsAppCredentialVault
    {
        public bool Removed { get; private set; }
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) { Removed = true; return Task.CompletedTask; }
    }

    private sealed class CapturingVault : IWhatsAppCredentialVault
    {
        public string? StoredSecret { get; private set; }
        public int StoreCalls { get; private set; }
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) { StoreCalls++; StoredSecret = secret.ToString(); return Task.FromResult(new WhatsAppCredentialReference("captured-reference")); }
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class StubHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
    }

    private sealed class MemoryStore : IWhatsAppEmbeddedSignupStore
    {
        public WhatsAppEmbeddedOnboardingDto? Item { get; set; } public bool FailAuthorization { get; set; }
        public Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default) { Item = onboarding; return Task.CompletedTask; }
        public Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Item?.IdOnboarding == id ? Item : null);
        public Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default) => Task.FromResult(Item?.IdBase == idBase ? Item : null);
        public Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string hash, int idBase, string user, DateTime now, CancellationToken ct = default)
        {
            if (Item is null || Item.IdBase != idBase || !string.Equals(Item.UsuarioIniciador, user, StringComparison.OrdinalIgnoreCase) || Item.StateHash != hash || Item.StateConsumedAtUtc is not null || Item.ExpiresAtUtc <= now || Item.Status != WhatsAppEmbeddedOnboardingStatus.Started) return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
            Item.StateConsumedAtUtc = now; return Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(Item);
        }
        public Task MarkAuthorizedAsync(Guid id, string reference, string business, CancellationToken ct = default) { if (FailAuthorization) throw new InvalidOperationException("store failure"); Item!.Status = WhatsAppEmbeddedOnboardingStatus.Authorized; Item.TokenReference = reference; return Task.CompletedTask; }
        public Task UpdateStatusAsync(Guid id, WhatsAppEmbeddedOnboardingStatus expected, WhatsAppEmbeddedOnboardingStatus next, string step, CancellationToken ct = default) { if (Item?.Status != expected) throw new InvalidOperationException(); Item.Status = next; Item.CurrentStep = step; return Task.CompletedTask; }
        public Task MarkActionRequiredAsync(Guid id, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incident, CancellationToken ct = default) => throw new NotSupportedException(); public Task MarkRetryableFailureAsync(Guid id, string code, string summary, string incident, DateTime next, CancellationToken ct = default) => throw new NotSupportedException(); public Task MarkFinalFailureAsync(Guid id, string code, string summary, string incident, CancellationToken ct = default) => throw new NotSupportedException(); public Task MarkReadyAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException(); public Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string worker, DateTime now, DateTime expires, CancellationToken ct = default) => throw new NotSupportedException(); public Task ReleaseClaimAsync(Guid id, string worker, DateTime? next, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
