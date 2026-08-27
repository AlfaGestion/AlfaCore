using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppTenantIsolationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task MissingSchemaWithEmbeddedDisabled_PreservesLegacy()
    {
        var store = new OwnershipStore(null, false);
        await new WhatsAppWebhookTenantGuard(store, Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = false }))
            .ValidateAsync(84, ["1195619520311268"]);

        var resolver = new WhatsAppRuntimeCredentialResolver(store, new Vault(null, string.Empty),
            Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = false }));
        var result = await resolver.ResolveAsync(84, null, "1195619520311268", Legacy());
        Assert.Equal(WhatsAppRuntimeCredentialOrigin.Legacy, result.Origin);
    }

    [Fact]
    public async Task MissingSchemaWithEmbeddedEnabled_FailsWithoutLegacyFallback()
    {
        var store = new OwnershipStore(null, false);
        var options = Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true });
        await Assert.ThrowsAsync<WhatsAppEmbeddedSchemaUnavailableException>(() =>
            new WhatsAppWebhookTenantGuard(store, options).ValidateAsync(84, ["1195619520311268"]));
        await Assert.ThrowsAsync<WhatsAppEmbeddedSchemaUnavailableException>(() =>
            new WhatsAppRuntimeCredentialResolver(store, new Vault(null, string.Empty), options)
                .ResolveAsync(84, null, "1195619520311268", Legacy()));
    }

    [Fact]
    public async Task CallbackBaseAAndOwnershipBaseB_IsRejectedBeforeTenantWork()
    {
        var guard = new WhatsAppWebhookTenantGuard(new OwnershipStore(new("9201", "9101", 2, DateTime.UtcNow)));
        var error = await Assert.ThrowsAsync<WhatsAppWebhookTenantMismatchException>(() => guard.ValidateAsync(1, ["9201"]));
        Assert.Equal(1, error.CallbackBaseId);
        Assert.Equal(2, error.OwnerBaseId);
    }

    [Fact]
    public async Task CallbackAndOwnershipSameBase_IsAccepted()
        => await new WhatsAppWebhookTenantGuard(new OwnershipStore(new("9201", "9101", 1, DateTime.UtcNow))).ValidateAsync(1, ["9201"]);

    [Fact]
    public async Task LegacyPhoneWithoutOwnership_RemainsCompatible()
        => await new WhatsAppWebhookTenantGuard(new OwnershipStore(null)).ValidateAsync(1, ["legacy-phone"]);

    [Fact]
    public async Task EmbeddedSignup_UsesVaultAndNeverLegacyToken()
    {
        var resolver = CreateResolver(new("9201", "9101", 1, DateTime.UtcNow), new("vault-ref"), "vault-token");
        var result = await resolver.ResolveAsync(1, 7, "9201", Legacy());
        Assert.Equal(WhatsAppRuntimeCredentialOrigin.EmbeddedSignup, result.Origin);
        Assert.Equal("vault-token", result.AccessToken);
        Assert.Equal("9101", result.WabaId);
    }

    [Fact]
    public async Task EmbeddedSignupWithoutVault_FailsWithoutLegacyFallback()
    {
        var resolver = CreateResolver(new("9201", "9101", 1, DateTime.UtcNow), null, "");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(1, 7, "9201", Legacy()));
        Assert.Contains("credencial segura", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TwoWabasInSameBase_ResolveCredentialByPhoneNumberId()
    {
        var store = new MultiOwnershipStore(new Dictionary<string, WhatsAppPhoneOwnership>
        {
            ["9201"] = new("9201", "9101", 1, DateTime.UtcNow),
            ["9202"] = new("9202", "9102", 1, DateTime.UtcNow)
        });
        var vault = new MultiVault();
        var resolver = new WhatsAppRuntimeCredentialResolver(store, vault, Options.Create(new WhatsAppEmbeddedSignupOptions { GraphApiVersion = "v26.0" }));
        Assert.Equal("token-9201", (await resolver.ResolveAsync(1, 1, "9201", Legacy())).AccessToken);
        Assert.Equal("token-9202", (await resolver.ResolveAsync(1, 2, "9202", Legacy())).AccessToken);
    }

    [Fact]
    public void WebhookGuardRunsBeforeAnyOperationalPersistenceAndAutomationUsesCommonSender()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var method = source.IndexOf("RegisterIncomingWebhookAsync", StringComparison.Ordinal);
        var guard = source.IndexOf("whatsAppWebhookTenantGuard.ValidateAsync", method, StringComparison.Ordinal);
        var log = source.IndexOf("InsertWebhookLogAsync(\"META_WHATSAPP\"", method, StringComparison.Ordinal);
        var conversation = source.IndexOf("EnsureConversationAsync(incoming", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && guard > method && log > guard && conversation > log);
        Assert.Contains("SistemaAccion = \"BIENVENIDA\"", source, StringComparison.Ordinal);
        Assert.Contains("await SendMessageAsync(new ConversacionSendMessageRequest", source, StringComparison.Ordinal);
        Assert.Contains("GetTemplatesForConversationAsync", source, StringComparison.Ordinal);
    }

    private static WhatsAppRuntimeCredentialResolver CreateResolver(WhatsAppPhoneOwnership? owner, WhatsAppCredentialReference? reference, string secret)
        => new(new OwnershipStore(owner), new Vault(reference, secret), Options.Create(new WhatsAppEmbeddedSignupOptions { GraphApiVersion = "v26.0" }));
    private static ConversacionWhatsAppConfigDto Legacy() => new() { AccessToken = "legacy-token", PhoneNumberId = "legacy", BusinessAccountId = "legacy-waba", ApiVersion = "v22.0" };

    private sealed class OwnershipStore(WhatsAppPhoneOwnership? phone, bool schemaAvailable = true) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(schemaAvailable);
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult(phone);
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string a, int b, string c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class MultiOwnershipStore(Dictionary<string, WhatsAppPhoneOwnership> phones) : IWhatsAppAssetOwnershipStore
    {
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult(phones.GetValueOrDefault(id));
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string id, CancellationToken ct = default) => Task.FromResult<WhatsAppWabaOwnership?>(null);
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string a, int b, string c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Vault(WhatsAppCredentialReference? reference, string secret) : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference?> FindActiveCredentialAsync(int a, string b, string c, CancellationToken ct = default) => Task.FromResult(reference);
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>(secret.AsMemory());
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class MultiVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference?> FindActiveCredentialAsync(int a, string b, string phone, CancellationToken ct = default) => Task.FromResult<WhatsAppCredentialReference?>(new($"ref-{phone}"));
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>(r.Value.Replace("ref-", "token-").AsMemory());
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext c, ReadOnlyMemory<char> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference r, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
