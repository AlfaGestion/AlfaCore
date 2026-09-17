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
        Assert.Contains("CALLBACK_PATH = /api/conversaciones/whatsapp/webhook/{token}", output);
        Assert.Contains("CALLBACK_QUERY_KEYS = hub.mode,hub.verify_token,hub.challenge", output);
        Assert.Contains("CALLBACK_RESPONSE_CONTENT_TYPE = ", output);
        Assert.Contains("CALLBACK_RESPONSE_KIND = CHALLENGE", output);
        Assert.Contains("CALLBACK_RESPONSE_MATCHES_CHALLENGE = True", output);
        Assert.Contains("EVIDENCE = APP_NOT_SUBSCRIBED", output);
        // El WebhookToken real ("base-token-secret" en CallbackUrl) nunca debe aparecer -- CALLBACK_PATH
        // lo enmascara siempre como "{token}".
        Assert.DoesNotContain("base-token-secret", output);
    }

    /// <summary>
    /// Regresión Base4271 (producción, 2026-09-16): el self-check reportó CALLBACK_HTTP=200 pero
    /// CALLBACK_REACHABLE=False ("el callback no devolvió el challenge esperado"). Un HTTP 200 con un
    /// body que NO es el challenge (típicamente la SPA de Blazor sirviendo su index.html por un
    /// fallback de ruta -- confirmado en esta sesión con una prueba real contra el host público) es
    /// indistinguible de "credenciales mal configuradas" sin esta clasificación. Este test fija el
    /// contrato: CALLBACK_RESPONSE_KIND debe decir HTML, no CHALLENGE, cuando el body es HTML.
    /// </summary>
    [Fact]
    public async Task CallbackReturns200WithHtmlBody_IsClassifiedAsHtmlNotChallenge()
    {
        const string htmlShell = "<!DOCTYPE html><html><head><title>AlfaNet - Alfa Gestión</title></head><body></body></html>";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "callback.test"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(htmlShell, System.Text.Encoding.UTF8, "text/html") }
                : Json(HttpStatusCode.OK, """{"data":[]}"""));
        var output = await RunAsync(handler);

        Assert.Contains("CALLBACK_HTTP = 200", output);
        Assert.Contains("CALLBACK_REACHABLE = False", output);
        Assert.Contains("CALLBACK_RESPONSE_CONTENT_TYPE = text/html; charset=utf-8", output);
        Assert.Contains($"CALLBACK_RESPONSE_LENGTH = {htmlShell.Length}", output);
        Assert.Contains("CALLBACK_RESPONSE_KIND = HTML", output);
        Assert.Contains("CALLBACK_RESPONSE_MATCHES_CHALLENGE = False", output);
        Assert.DoesNotContain(htmlShell, output);
    }

    [Fact]
    public async Task CallbackReturnsEmptyBody_IsClassifiedAsEmpty()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "callback.test"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) }
                : Json(HttpStatusCode.OK, """{"data":[]}"""));
        var output = await RunAsync(handler);

        Assert.Contains("CALLBACK_RESPONSE_KIND = EMPTY", output);
        Assert.Contains("CALLBACK_RESPONSE_LENGTH = 0", output);
    }

    [Fact]
    public void RoutingSource_VerifyTokenSourceAndWebhookRouteMatched_AreExposedWithoutPrintingValues()
    {
        var tenantSourced = BuildRoutingSourceInspection(
            IdBase, string.Empty, "https://global.example.com", "some-verify-token", "some-webhook-token",
            tenantVerifyTokenPresent: true, webhookRouteMatched: true);
        Assert.Equal("TENANT", tenantSourced.VerifyTokenSource);
        Assert.True(tenantSourced.WebhookRouteMatched);

        var globalSourced = BuildRoutingSourceInspection(
            IdBase, string.Empty, "https://global.example.com", "some-verify-token", "some-webhook-token",
            tenantVerifyTokenPresent: false, webhookRouteMatched: false);
        Assert.Equal("GLOBAL", globalSourced.VerifyTokenSource);
        Assert.False(globalSourced.WebhookRouteMatched);

        var none = BuildRoutingSourceInspection(IdBase, string.Empty, string.Empty, string.Empty, string.Empty);
        Assert.Equal("NONE", none.VerifyTokenSource);
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
    public async Task RoutingSource_TenantUrlValid_SelectsTenant()
    {
        var output = await RunWithRoutingSourceAsync(
            tenantUrl: "https://tenant.example.com/root/path?token=query-secret",
            globalUrl: "https://global.example.com",
            verifyToken: VerifyToken,
            webhookToken: "webhook-token-secret");

        Assert.Contains("TENANT_PUBLIC_BASE_URL_PRESENT = True", output);
        Assert.Contains("TENANT_PUBLIC_BASE_URL_VALUE_SANITIZED = https://tenant.example.com/root/path", output);
        Assert.Contains("TENANT_PUBLIC_BASE_URL_IS_ABSOLUTE = True", output);
        Assert.Contains("TENANT_PUBLIC_BASE_URL_IS_HTTPS = True", output);
        Assert.Contains("SELECTED_ROUTING_SOURCE = TENANT_PUBLIC_BASE_URL", output);
        Assert.Contains("EFFECTIVE_PUBLIC_BASE_URL_VALID = True", output);
        Assert.Contains("ROUTING_FAILURE_REASON = N/A", output);
        Assert.DoesNotContain("query-secret", output);
        Assert.DoesNotContain("webhook-token-secret", output);
    }

    [Fact]
    public async Task RoutingSource_TenantEmptyAndGlobalValid_SelectsGlobal()
    {
        var output = await RunWithRoutingSourceAsync(
            tenantUrl: "",
            globalUrl: "https://global.example.com/base",
            verifyToken: VerifyToken,
            webhookToken: "webhook-token-secret");

        Assert.Contains("TENANT_PUBLIC_BASE_URL_PRESENT = False", output);
        Assert.Contains("GLOBAL_CALLBACK_BASE_URL_PRESENT = True", output);
        Assert.Contains("GLOBAL_CALLBACK_BASE_URL_VALUE_SANITIZED = https://global.example.com/base", output);
        Assert.Contains("SELECTED_ROUTING_SOURCE = GLOBAL_CALLBACK_BASE_URL", output);
        Assert.Contains("EFFECTIVE_PUBLIC_BASE_URL_SANITIZED = https://global.example.com/base", output);
        Assert.Contains("EFFECTIVE_PUBLIC_BASE_URL_VALID = True", output);
    }

    [Fact]
    public async Task RoutingSource_TenantInvalidNonEmptyDoesNotFallbackToGlobal()
    {
        var output = await RunWithRoutingSourceAsync(
            tenantUrl: "http://tenant.example.com",
            globalUrl: "https://global.example.com",
            verifyToken: VerifyToken,
            webhookToken: "webhook-token-secret");

        Assert.Contains("TENANT_PUBLIC_BASE_URL_PRESENT = True", output);
        Assert.Contains("TENANT_PUBLIC_BASE_URL_IS_ABSOLUTE = True", output);
        Assert.Contains("TENANT_PUBLIC_BASE_URL_IS_HTTPS = False", output);
        Assert.Contains("GLOBAL_CALLBACK_BASE_URL_IS_HTTPS = True", output);
        Assert.Contains("SELECTED_ROUTING_SOURCE = TENANT_PUBLIC_BASE_URL", output);
        Assert.Contains("EFFECTIVE_PUBLIC_BASE_URL_VALID = False", output);
        Assert.Contains("ROUTING_FAILURE_REASON = TENANT_PUBLIC_BASE_URL_NOT_HTTPS", output);
        Assert.Contains("CALLBACK_ROUTING_RESOLVED = False", output);
    }

    [Fact]
    public async Task RoutingSource_TenantEmptyAndGlobalInvalid_FailsOnGlobal()
    {
        var output = await RunWithRoutingSourceAsync(
            tenantUrl: "",
            globalUrl: "http://global.example.com",
            verifyToken: VerifyToken,
            webhookToken: "webhook-token-secret");

        Assert.Contains("SELECTED_ROUTING_SOURCE = GLOBAL_CALLBACK_BASE_URL", output);
        Assert.Contains("GLOBAL_CALLBACK_BASE_URL_IS_HTTPS = False", output);
        Assert.Contains("EFFECTIVE_PUBLIC_BASE_URL_VALID = False", output);
        Assert.Contains("ROUTING_FAILURE_REASON = GLOBAL_CALLBACK_BASE_URL_NOT_HTTPS", output);
    }

    [Fact]
    public async Task RoutingSource_VerifyTokenMissing_IsReportedWithoutPrintingToken()
    {
        var output = await RunWithRoutingSourceAsync(
            tenantUrl: "https://tenant.example.com",
            globalUrl: "",
            verifyToken: "",
            webhookToken: "webhook-token-secret");

        Assert.Contains("VERIFY_TOKEN_PRESENT = False", output);
        Assert.Contains("WEBHOOK_TOKEN_PRESENT = True", output);
        Assert.Contains("ROUTING_FAILURE_REASON = VERIFY_TOKEN_MISSING", output);
        Assert.DoesNotContain("webhook-token-secret", output);
    }

    [Fact]
    public async Task RoutingSource_RedactsQueryTokensAndSecrets()
    {
        const string querySecret = "query-token-never-printed";
        const string webhookSecret = "webhook-token-never-printed";
        const string verifySecret = "verify-token-never-printed";
        var output = await RunWithRoutingSourceAsync(
            tenantUrl: "https://tenant.example.com/webhook?access_token=" + querySecret,
            globalUrl: "https://global.example.com?token=" + querySecret,
            verifyToken: verifySecret,
            webhookToken: webhookSecret);

        Assert.Contains("TENANT_PUBLIC_BASE_URL_VALUE_SANITIZED = https://tenant.example.com/webhook", output);
        Assert.Contains("GLOBAL_CALLBACK_BASE_URL_VALUE_SANITIZED = https://global.example.com/", output);
        Assert.DoesNotContain(querySecret, output);
        Assert.DoesNotContain(webhookSecret, output);
        Assert.DoesNotContain(verifySecret, output);
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

    private static async Task<string> RunWithRoutingSourceAsync(string tenantUrl, string globalUrl, string verifyToken, string webhookToken)
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host.EndsWith(".example.com", StringComparison.Ordinal)
                ? CallbackSuccess(request)
                : Json(HttpStatusCode.OK, """{"data":[]}"""));
        using var client = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await ExecuteAsync(
            IdBase,
            Phone,
            Waba,
            ExpectedAppId,
            new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, IdBase, DateTime.UtcNow)),
            new FakeCredentialResolver(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", AccessToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup)),
            new ThrowingRoutingProvider(new InvalidOperationException("No deberia usarse cuando hay diagnostico de source.")),
            client,
            "https://graph.facebook.com",
            output,
            CancellationToken.None,
            (_, _) => Task.FromResult(BuildRoutingSourceInspection(IdBase, tenantUrl, globalUrl, verifyToken, webhookToken)));

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
