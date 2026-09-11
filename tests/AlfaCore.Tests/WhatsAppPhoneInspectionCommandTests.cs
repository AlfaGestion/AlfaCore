using System.Net;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// --inspect-whatsapp-phone: comando one-shot 100% read-only (ownership → credencial vía
/// WhatsAppRuntimeCredentialResolver, sin fallback legacy → un único GET a Graph). Prueba el núcleo
/// testeable (ExecuteAsync) con dobles de ownership/credencial y un HttpMessageHandler que registra
/// si Graph llegó a invocarse -- nunca toca SQL/Vault/Graph reales.
/// </summary>
public sealed class WhatsAppPhoneInspectionCommandTests
{
    private const int Base4264 = 4264;
    private const string Waba = "2597305014055622";
    private const string Phone = "1362965780228889";
    private const string SecretToken = "secret-access-token-xyz-never-printed";

    [Fact]
    public async Task Ownership_Correct_ProceedsToGraphAndReturnsFields()
    {
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, Base4264, DateTime.UtcNow));
        var resolver = new FakeCredentialResolver(new WhatsAppRuntimeCredential(
            Waba, Phone, "v26.0", SecretToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup));
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"1362965780228889","platform_type":"CLOUD_API","is_on_biz_app":true,
                 "display_phone_number":"+1 555-365-8051","verified_name":"AlfaNetPapelera","quality_rating":"GREEN"}
                """)
        });
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await WhatsAppPhoneInspectionCommand.ExecuteAsync(
            Base4264, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com", output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(handler.Called);
        var text = output.ToString();
        Assert.Contains("OWNERSHIP = OK", text, StringComparison.Ordinal);
        Assert.Contains("VAULT_CREDENTIAL = OK", text, StringComparison.Ordinal);
        Assert.Contains("GRAPH HTTP = 200", text, StringComparison.Ordinal);
        Assert.Contains("platform_type = CLOUD_API", text, StringComparison.Ordinal);
        Assert.Contains("is_on_biz_app = True", text, StringComparison.Ordinal);
        Assert.Contains("display_phone_number = +1 555-365-8051", text, StringComparison.Ordinal);
        Assert.Contains("verified_name = AlfaNetPapelera", text, StringComparison.Ordinal);
        Assert.Contains("quality_rating = GREEN", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingOwnership_IsBlockedBeforeGraph()
    {
        var ownershipStore = new FakeOwnershipStore(ownership: null);
        var resolver = new ThrowingCredentialResolver(new InvalidOperationException("No debería invocarse: sin ownership el flujo corta antes."));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Graph no debería llamarse."));
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await WhatsAppPhoneInspectionCommand.ExecuteAsync(
            Base4264, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com", output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        var text = output.ToString();
        Assert.Contains("OWNERSHIP = ERROR", text, StringComparison.Ordinal);
        Assert.Contains("VAULT_CREDENTIAL = N/A", text, StringComparison.Ordinal);
        Assert.Contains("GRAPH HTTP = N/A", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTenantOwnership_IsBlockedBeforeGraph()
    {
        // El PhoneNumberId pertenece a Base84, no a Base4264: debe cortar por cross-tenant sin
        // resolver credencial ni llamar a Graph.
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, IdBase: 84, DateTime.UtcNow));
        var resolver = new ThrowingCredentialResolver(new InvalidOperationException("No debería invocarse: cross-tenant corta antes."));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Graph no debería llamarse."));
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await WhatsAppPhoneInspectionCommand.ExecuteAsync(
            Base4264, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com", output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        var text = output.ToString();
        Assert.Contains("OWNERSHIP = ERROR", text, StringComparison.Ordinal);
        Assert.Contains("cross-tenant", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VaultUnavailable_FailsControlled_NeverCallsGraph()
    {
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, Base4264, DateTime.UtcNow));
        var resolver = new ThrowingCredentialResolver(
            new WhatsAppEmbeddedVaultUnavailableException("La credencial segura del número Embedded Signup no está disponible."));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Graph no debería llamarse."));
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await WhatsAppPhoneInspectionCommand.ExecuteAsync(
            Base4264, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com", output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        var text = output.ToString();
        Assert.Contains("OWNERSHIP = OK", text, StringComparison.Ordinal);
        Assert.Contains("VAULT_CREDENTIAL = ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolverFallsBackToLegacyDespiteOwnership_IsRejected_NeverUsed()
    {
        // Defensa en profundidad: si el resolver alguna vez devolviera Legacy pese al ownership ES
        // confirmado arriba (no debería pasar dado el diseño de WhatsAppRuntimeCredentialResolver),
        // el comando igual debe rechazarlo y nunca usar esa credencial contra Graph.
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, Base4264, DateTime.UtcNow));
        var resolver = new FakeCredentialResolver(new WhatsAppRuntimeCredential(
            "legacy-waba", Phone, "v22.0", "legacy-secret-token", WhatsAppRuntimeCredentialOrigin.Legacy));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Graph no debería llamarse."));
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await WhatsAppPhoneInspectionCommand.ExecuteAsync(
            Base4264, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com", output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        Assert.Contains("VAULT_CREDENTIAL = ERROR", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessToken_NeverAppearsInOutput()
    {
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, Base4264, DateTime.UtcNow));
        var resolver = new FakeCredentialResolver(new WhatsAppRuntimeCredential(
            Waba, Phone, "v26.0", SecretToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup));
        string? capturedAuthHeader = null;
        var handler = new RecordingHandler(request =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"platform_type\":\"CLOUD_API\"}") };
        });
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        await WhatsAppPhoneInspectionCommand.ExecuteAsync(
            Base4264, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com", output, CancellationToken.None);

        // El token sí viajó en el header real (confirma que el GET se autenticó correctamente)...
        Assert.Contains(SecretToken, capturedAuthHeader);
        // ...pero jamás aparece en lo que el comando imprime.
        Assert.DoesNotContain(SecretToken, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", output.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeOwnershipStore(WhatsAppPhoneOwnership? ownership) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Task.FromResult(ownership);
    }

    private sealed class FakeCredentialResolver(WhatsAppRuntimeCredential credential) : IWhatsAppRuntimeCredentialResolver
    {
        public Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default)
            => Task.FromResult(credential);
    }

    private sealed class ThrowingCredentialResolver(Exception exception) : IWhatsAppRuntimeCredentialResolver
    {
        public Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default)
            => Task.FromException<WhatsAppRuntimeCredential>(exception);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public bool Called { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(responseFactory(request));
        }
    }
}
