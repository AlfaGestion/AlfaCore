using System.Net;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public sealed class MetaWhatsAppManagementClientTests
{
    [Fact]
    public async Task DiscoverySupportsMultipleBusinessesWabasAndPhones()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v26.0/me/businesses" => Json("{\"data\":[{\"id\":\"9001\",\"name\":\"Business A\"},{\"id\":\"9002\",\"name\":\"Business B\"}]}"),
            "/v26.0/9001/owned_whatsapp_business_accounts" => Json("{\"data\":[{\"id\":\"9101\",\"name\":\"WABA A\"}]}"),
            "/v26.0/9001/client_whatsapp_business_accounts" => Json("{\"data\":[]}"),
            "/v26.0/9101/phone_numbers" => Json("{\"data\":[{\"id\":\"9201\",\"display_phone_number\":\"+1 555 100\",\"verified_name\":\"Uno\",\"quality_rating\":\"GREEN\",\"platform_type\":\"CLOUD_API\"},{\"id\":\"9202\",\"display_phone_number\":\"+1 555 200\",\"verified_name\":\"Dos\",\"quality_rating\":\"UNKNOWN\",\"platform_type\":\"NOT_APPLICABLE\"}]}"),
            _ => Json("{\"data\":[]}")
        });
        var client = Create(handler);
        var token = new WhatsAppCredentialReference("ref");

        var businesses = await client.DiscoverAuthorizedBusinessesAsync(token);
        var wabas = await client.DiscoverWabasAsync("9001", token);
        var phones = await client.DiscoverPhoneNumbersAsync("9101", token);

        Assert.Equal(2, businesses.Count);
        Assert.Single(wabas);
        Assert.Equal(2, phones.Count);
        Assert.Equal(MetaPhoneRegistrationStatus.Registered, phones[0].RegistrationStatus);
        Assert.Equal(MetaPhoneRegistrationStatus.RegistrationRequired, phones[1].RegistrationStatus);
    }

    [Fact]
    public async Task TemplateDiscoveryIsScopedToRequestedWaba()
    {
        string? requestedPath = null;
        var client = Create(new RoutingHandler(request =>
        {
            requestedPath = request.RequestUri!.AbsolutePath;
            return Json("{\"data\":[{\"id\":\"9301\",\"name\":\"bienvenida\",\"language\":\"es_AR\",\"status\":\"APPROVED\",\"category\":\"UTILITY\",\"components\":[{\"type\":\"BODY\",\"text\":\"Hola {{1}}\"}]}]}");
        }));
        var result = await client.DiscoverTemplatesAsync("9102", new("ref"));
        Assert.Equal("/v26.0/9102/message_templates", requestedPath);
        Assert.Single(result);
        Assert.Equal("APPROVED", result[0].Status);
    }

    [Fact]
    public async Task SubscriptionIsIdempotentAndPostsOnlyWhenMissing()
    {
        var subscribed = true;
        var correctOverride = true;
        var posts = 0;
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "callback.test")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.Query.Split("hub.challenge=", StringSplitOptions.None)[1].Split('&')[0]) };
            if (request.Method == HttpMethod.Post) { posts++; subscribed = true; correctOverride = request.Content is not null; return Json("{\"success\":true}"); }
            return Json(subscribed
                ? $"{{\"data\":[{{\"id\":\"999\",\"override_callback_uri\":\"{(correctOverride ? "https://callback.test/webhook/token" : "") }\"}}]}}"
                : "{\"data\":[]}");
        });
        var client = Create(handler);
        var token = new WhatsAppCredentialReference("ref");

        await client.EnsureWabaSubscriptionAsync("9101", 1, token);
        Assert.Equal(0, posts);
        subscribed = false;
        correctOverride = false;
        await client.EnsureWabaSubscriptionAsync("9101", 1, token);
        Assert.Equal(2, posts);
    }

    [Fact]
    public async Task RevokedCredentialProducesControlledReauthorizationError()
    {
        var client = Create(new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"code\":190}}") }));
        var error = await Assert.ThrowsAsync<MetaWhatsAppManagementException>(() => client.DiscoverPhoneNumbersAsync("9101", new("ref")));
        Assert.True(error.RequiresReauthorization);
        Assert.False(error.IsTransient);
        Assert.Equal("190", error.ErrorCode);
    }

    [Fact]
    public async Task RegisterPhoneUsesProtectedPinAndConfiguredGraphVersion()
    {
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedScheme = null;
        string? capturedBody = null;
        var handler = new AsyncRoutingHandler(async request =>
        {
            capturedMethod = request.Method;
            capturedPath = request.RequestUri!.AbsolutePath;
            capturedScheme = request.Headers.Authorization!.Scheme;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return Json("{\"success\":true}");
        });
        var client = Create(handler);

        await client.RegisterPhoneAsync("9201", new("pin-ref"), new("credential-ref"));

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/v26.0/9201/register", capturedPath);
        Assert.Equal("Bearer", capturedScheme);
        Assert.Contains("messaging_product=whatsapp", capturedBody, StringComparison.Ordinal);
        Assert.Contains("pin=123456", capturedBody, StringComparison.Ordinal);
    }

    private static MetaWhatsAppManagementClient Create(HttpMessageHandler handler)
        => new(new SingleClientFactory(new HttpClient(handler)), new FakeVault(), new FakePinVault(), new FakeRoutingProvider(), Options.Create(new WhatsAppEmbeddedSignupOptions
        {
            AppId = "999", SystemUserId = "998", GraphApiVersion = "v26.0", GraphBaseUrl = "https://graph.facebook.com"
        }));

    private sealed class FakeRoutingProvider : IWhatsAppWabaRoutingProvider
    {
        public Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(new WhatsAppWabaRoutingConfiguration("https://callback.test/webhook/token", "verify"));
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class FakeVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>("test-business-credential".AsMemory());
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePinVault : IWhatsAppPhonePinVault
    {
        public Task<WhatsAppPhonePinReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> pin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default) => Task.FromResult<ReadOnlyMemory<char>>("123456".AsMemory());
        public Task RemoveAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    private sealed class AsyncRoutingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responseFactory(request);
    }
}
