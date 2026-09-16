using System.Net;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;
using static AlfaCore.Configuration.WhatsAppSubscriptionInspectionCommand;

namespace AlfaCore.Tests;

public sealed class WhatsAppSubscriptionInspectionCommandTests
{
    private const int IdBase = 4271;
    private const string Phone = "1373763429148369";
    private const string Waba = "1760901255041127";
    private const string ExpectedAppId = "1436083307772786";
    private const string AccessToken = "secret-runtime-token-never-printed";
    private const string VerifyToken = "secret-verify-token-never-printed";
    private const string CallbackUrl = "https://callback.test/api/conversaciones/whatsapp/webhook/base-token-secret";

    [Fact]
    public async Task CallbackSuccess_PrintsReachable()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "callback.test"
                ? CallbackSuccess(request)
                : Json(HttpStatusCode.OK, """{"data":[]}"""));
        var output = await RunAsync(handler);

        Assert.Contains("CALLBACK_ROUTING_RESOLVED = True", output);
        Assert.Contains("CALLBACK_HTTP = 200", output);
        Assert.Contains("CALLBACK_REACHABLE = True", output);
        Assert.Contains("EVIDENCE = APP_NOT_SUBSCRIBED", output);
    }

    [Fact]
    public async Task CallbackHttpRequestException_IsReportedSanitized()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "callback.test")
                throw new HttpRequestException("No route to https://callback.test/webhook?hub.verify_token=super-secret-token");
            return Json(HttpStatusCode.OK, """{"data":[]}""");
        });

        var output = await RunAsync(handler);

        Assert.Contains("CALLBACK_ERROR_TYPE = HttpRequestException", output);
        Assert.Contains("CALLBACK_REACHABLE = False", output);
        Assert.Contains("EVIDENCE = CALLBACK_SELF_CHECK_FAILED", output);
        Assert.DoesNotContain("super-secret-token", output);
        Assert.Contains("hub.verify_token=[REDACTED]", output);
    }

    [Fact]
    public async Task CallbackTimeout_IsReportedAsTimeout()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "callback.test")
                throw new TaskCanceledException("request timed out with hub.verify_token=timeout-secret");
            return Json(HttpStatusCode.OK, """{"data":[]}""");
        });

        var output = await RunAsync(handler);

        Assert.Contains("CALLBACK_ERROR_TYPE = Timeout", output);
        Assert.Contains("CALLBACK_REACHABLE = False", output);
        Assert.Contains("EVIDENCE = CALLBACK_SELF_CHECK_FAILED", output);
        Assert.DoesNotContain("timeout-secret", output);
    }

    [Fact]
    public async Task RoutingFail_IsReportedBeforeCallbackGet()
    {
        var handler = new RoutingHandler(_ => Json(HttpStatusCode.OK, """{"data":[]}"""));
        using var client = new HttpClient(handler);
        var output = new StringWriter();

        await ExecuteAsync(
            IdBase,
            Phone,
            Waba,
            ExpectedAppId,
            new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, IdBase, DateTime.UtcNow)),
            new FakeCredentialResolver(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", AccessToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup)),
            new ThrowingRoutingProvider(new InvalidOperationException("La Base publica HTTPS no es valida.")),
            client,
            "https://graph.facebook.com",
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Contains("CALLBACK_ROUTING_RESOLVED = False", text);
        Assert.Contains("CALLBACK_ERROR_TYPE = InvalidOperationException", text);
        Assert.Contains("EVIDENCE = ROUTING_CONFIGURATION_FAILED", text);
        Assert.True(handler.Called); // subscribed_apps igual se consulta read-only con la credencial ya validada.
    }

    [Fact]
    public async Task SubscribedAppFound_ConfirmsExpectedApp()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "callback.test"
                ? CallbackSuccess(request)
                : Json(HttpStatusCode.OK, $$"""{"data":[{"id":"{{ExpectedAppId}}","override_callback_uri":"{{CallbackUrl}}"}]}"""));

        var output = await RunAsync(handler);

        Assert.Contains("SUBSCRIBED_APPS_COUNT = 1", output);
        Assert.Contains("EXPECTED_APP_ID_FOUND = True", output);
        Assert.Contains("APP_ALREADY_SUBSCRIBED = True", output);
        Assert.Contains("EVIDENCE = APP_ALREADY_SUBSCRIBED", output);
    }

    [Fact]
    public async Task AppAbsent_IsReportedAsNotSubscribed()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "callback.test"
                ? CallbackSuccess(request)
                : Json(HttpStatusCode.OK, """{"data":[]}"""));

        var output = await RunAsync(handler);

        Assert.Contains("SUBSCRIBED_APPS_COUNT = 0", output);
        Assert.Contains("EXPECTED_APP_ID_FOUND = False", output);
        Assert.Contains("APP_ALREADY_SUBSCRIBED = False", output);
        Assert.Contains("EVIDENCE = APP_NOT_SUBSCRIBED", output);
    }

    [Fact]
    public async Task GraphNon2xx_IsReportedWithGraphError()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "callback.test"
                ? CallbackSuccess(request)
                : Json(HttpStatusCode.Forbidden, """{"error":{"code":10,"type":"OAuthException","message":"Permission denied"}}"""));

        var output = await RunAsync(handler);

        Assert.Contains("SUBSCRIBED_APPS_HTTP = 403", output);
        Assert.Contains("ERROR_CODE = 10", output);
        Assert.Contains("ERROR_TYPE = OAuthException", output);
        Assert.Contains("ERROR_SUMMARY = Permission denied", output);
        Assert.Contains("EVIDENCE = GRAPH_SUBSCRIBED_APPS_FAILED", output);
    }

    [Fact]
    public async Task CallbackTokenRedaction_NeverPrintsTokens()
    {
        const string queryToken = "query-token-never-printed";
        var callbackWithQuery = CallbackUrl + "?token=" + queryToken;
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "callback.test"
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad challenge") }
                : Json(HttpStatusCode.OK, $$"""{"data":[{"id":"{{ExpectedAppId}}","override_callback_uri":"{{callbackWithQuery}}"}]}"""));

        var output = await RunAsync(handler, callbackUrl: callbackWithQuery);

        Assert.DoesNotContain(AccessToken, output);
        Assert.DoesNotContain(VerifyToken, output);
        Assert.DoesNotContain(queryToken, output);
        Assert.Contains("CALLBACK_HOST = callback.test", output);
    }

    [Fact]
    public async Task CrossTenantOwnership_IsBlockedBeforeVaultRoutingAndGraph()
    {
        var resolver = new ThrowingCredentialResolver(new InvalidOperationException("Vault no deberia llamarse."));
        var routingProvider = new ThrowingRoutingProvider(new InvalidOperationException("Routing no deberia llamarse."));
        var handler = new RoutingHandler(_ => throw new InvalidOperationException("Graph no deberia llamarse."));
        using var client = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await ExecuteAsync(
            IdBase,
            Phone,
            Waba,
            ExpectedAppId,
            new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, 9999, DateTime.UtcNow)),
            resolver,
            routingProvider,
            client,
            "https://graph.facebook.com",
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        var text = output.ToString();
        Assert.Contains("OWNERSHIP = ERROR", text);
        Assert.Contains("EVIDENCE = OWNERSHIP_BLOCKED", text);
    }

    [Fact]
    public void IsRequested_MatchesVerbCaseInsensitive()
    {
        Assert.True(WhatsAppSubscriptionInspectionCommand.IsRequested(["--inspect-whatsapp-subscription"]));
        Assert.True(WhatsAppSubscriptionInspectionCommand.IsRequested(["--INSPECT-WHATSAPP-SUBSCRIPTION"]));
        Assert.False(WhatsAppSubscriptionInspectionCommand.IsRequested(["--inspect-whatsapp-runtime"]));
    }

    private static async Task<string> RunAsync(HttpMessageHandler handler, string callbackUrl = CallbackUrl)
    {
        using var client = new HttpClient(handler);
        var output = new StringWriter();
        var exitCode = await ExecuteAsync(
            IdBase,
            Phone,
            Waba,
            ExpectedAppId,
            new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, IdBase, DateTime.UtcNow)),
            new FakeCredentialResolver(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", AccessToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup)),
            new FakeRoutingProvider(callbackUrl, VerifyToken),
            client,
            "https://graph.facebook.com",
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        return output.ToString();
    }

    private static HttpResponseMessage CallbackSuccess(HttpRequestMessage request)
    {
        var challenge = ReadQueryValue(request.RequestUri!, "hub.challenge");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(challenge) };
    }

    private static string ReadQueryValue(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length == 2 && string.Equals(Uri.UnescapeDataString(pieces[0]), key, StringComparison.Ordinal))
                return Uri.UnescapeDataString(pieces[1]);
        }

        return string.Empty;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json) };

    private sealed class FakeRoutingProvider(string callbackUrl, string verifyToken) : IWhatsAppWabaRoutingProvider
    {
        public Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(new WhatsAppWabaRoutingConfiguration(callbackUrl, verifyToken));
    }

    private sealed class ThrowingRoutingProvider(Exception exception) : IWhatsAppWabaRoutingProvider
    {
        public Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default)
            => Task.FromException<WhatsAppWabaRoutingConfiguration>(exception);
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

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public bool Called { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(responseFactory(request));
        }
    }
}
