using System.Net;
using System.Text.Json;
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
        // Un único POST (con override_callback_uri + verify_token) cubre tanto "nunca suscripta" como
        // "suscripta con callback incorrecto" -- ver auditoría Base4264, EnsureWabaSubscriptionAsync
        // ya no hace un POST "desnudo" previo.
        Assert.Equal(1, posts);
    }

    // --- parser tolerante de "id" en subscribed_apps (Base4264 / META_INVALID_ASSET) --------------

    private static HttpResponseMessage CallbackOrSubscribedApps(HttpRequestMessage request, string subscribedAppsDataJson, int posts)
    {
        if (request.RequestUri!.Host == "callback.test")
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri.Query.Split("hub.challenge=", StringSplitOptions.None)[1].Split('&')[0]) };
        if (request.Method == HttpMethod.Post)
            return Json("{\"success\":true}");
        return Json($"{{\"data\":[{subscribedAppsDataJson}]}}");
    }

    [Fact]
    public async Task SubscribedApps_IdAsJsonString_IsAccepted()
    {
        var client = Create(new RoutingHandler(r => CallbackOrSubscribedApps(r,
            "{\"id\":\"999\",\"override_callback_uri\":\"https://callback.test/webhook/token\"}", 0)));

        await client.EnsureWabaSubscriptionAsync("9101", 1, new("ref"));   // no exception => ya suscripta y confirmada
    }

    [Fact]
    public async Task SubscribedApps_IdAsJsonNumber_IsAccepted()
    {
        // Meta puede devolver "id" como número JSON en vez de string; debe normalizarse igual.
        var client = Create(new RoutingHandler(r => CallbackOrSubscribedApps(r,
            "{\"id\":999,\"override_callback_uri\":\"https://callback.test/webhook/token\"}", 0)));

        await client.EnsureWabaSubscriptionAsync("9101", 1, new("ref"));
    }

    [Fact]
    public void TryParseMetaId_LargeJsonNumber_ProducesExactDigitString_NoDoubleRoundTrip()
    {
        // Prueba directa y unitaria (no end-to-end) de la precisión exacta pedida: JsonElement
        // .TryGetInt64 parsea el long directamente del texto UTF8 del token, sin pasar por
        // double/float, así que un WABA id real de 16 dígitos sale exactamente igual, sin notación
        // científica ni redondeo.
        using var document = JsonDocument.Parse("""{"id":2597305014055622,"override_callback_uri":"https://callback.test/webhook/token"}""");

        var ok = AlfaCore.Services.MetaWhatsAppManagementClient.TryParseMetaId(document.RootElement, "id", out var id, out var kind, out var present);

        Assert.True(ok);
        Assert.True(present);
        Assert.Equal(JsonValueKind.Number, kind);
        Assert.Equal("2597305014055622", id);
    }

    [Fact]
    public async Task SubscribedApps_IdAsLargeJsonNumber_RoundTripsExactly_NoDoublePrecisionLoss()
    {
        // WABA real del incidente Base4264: 2597305014055622. Un long -> double -> string hubiera
        // podido perder precisión o pasar a notación científica; TryParseMetaId debe usar
        // JsonElement.TryGetInt64 (exacto) y no GetDouble/GetSingle.
        const string realWabaAppId = "2597305014055622";
        var posts = 0;
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "callback.test")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(request.RequestUri.Query.Split("hub.challenge=", StringSplitOptions.None)[1].Split('&')[0]) };
            if (request.Method == HttpMethod.Post) { posts++; return Json("{\"success\":true}"); }
            // "id" como JSON number sin comillas, exactamente el valor real del incidente.
            return Json($"{{\"data\":[{{\"id\":{realWabaAppId},\"override_callback_uri\":\"https://callback.test/webhook/token\"}}]}}");
        });
        var client = new MetaWhatsAppManagementClient(new SingleClientFactory(new HttpClient(handler)), new FakeVault(), new FakePinVault(), new FakeRoutingProvider(),
            Options.Create(new WhatsAppEmbeddedSignupOptions { AppId = realWabaAppId, SystemUserId = "998", GraphApiVersion = "v26.0", GraphBaseUrl = "https://graph.facebook.com" }));

        await client.EnsureWabaSubscriptionAsync("2597305014055622", 4264, new("ref"));

        // Si el id se hubiera parseado con pérdida de precisión (double) o notación distinta, la
        // comparación de strings habría fallado y el código habría re-suscripto innecesariamente.
        Assert.Equal(0, posts);
    }

    // --- fallback de identidad por callback cuando Meta omite "id" (Base4264, segundo incidente) ---

    [Fact]
    public async Task SubscribedApps_MissingIdWithMatchingCallback_IsAcceptedAsEvidence()
    {
        // Causa raíz confirmada en producción: Meta puede responder un ítem de subscribed_apps SIN
        // "id" pero con override_callback_uri utilizable. Antes se clasificaba como malformado y
        // terminaba en META_SUBSCRIBED_APPS_UNPARSEABLE aunque el callback ya fuera exactamente el
        // nuestro. Ahora cuenta como evidencia válida y confirma en el primer GET (sin reintento).
        var posts = 0;
        var client = Create(new RoutingHandler(r => CallbackOrSubscribedApps(r,
            "{\"override_callback_uri\":\"https://callback.test/webhook/token\"}", posts)));   // sin "id"

        await client.EnsureWabaSubscriptionAsync("9101", 1, new("ref"));   // no exception => confirmado por callback
    }

    [Fact]
    public async Task SubscribedApps_MissingIdAndCallback_FailsControlled_WithSanitizedDiagnostic()
    {
        // Sin id NI callback utilizable no hay ninguna evidencia posible: sigue siendo fail-controlled,
        // con diagnóstico sanitizado (nunca token, nunca body completo).
        var diagnosticsDir = Path.Combine(AppContext.BaseDirectory, "diagnostics");
        var diagnosticsFile = Path.Combine(diagnosticsDir, $"meta-asset-parse-failures-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        var before = File.Exists(diagnosticsFile) ? File.ReadAllText(diagnosticsFile) : string.Empty;

        var client = Create(new RoutingHandler(r => CallbackOrSubscribedApps(r,
            "{\"note\":\"ni id ni callback\"}", 0)));

        var error = await Assert.ThrowsAsync<MetaWhatsAppManagementException>(
            () => client.EnsureWabaSubscriptionAsync("9101", 1, new("ref")));

        Assert.Equal("META_SUBSCRIBED_APPS_UNPARSEABLE", error.ErrorCode);
        Assert.False(error.IsTransient);
        Assert.False(error.RequiresReauthorization);

        var after = File.ReadAllText(diagnosticsFile);
        var appended = after[before.Length..];
        Assert.Contains("\"Endpoint\":\"subscribed_apps\"", appended, StringComparison.Ordinal);
        Assert.Contains("\"IdPresent\":false", appended, StringComparison.Ordinal);
        Assert.Contains("\"IdValueKind\":\"Undefined\"", appended, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", appended, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ref", appended, StringComparison.Ordinal);   // el token del vault nunca se loguea
    }

    [Fact]
    public async Task SubscribedApps_ExplicitOtherAppId_IsNeverTreatedAsOurs_EvenWithMatchingCallback()
    {
        // Seguridad: un id explícito de OTRA app nunca debe considerarse nuestro, ni siquiera si su
        // callback coincide exactamente con el esperado -- el fallback de callback sólo aplica cuando
        // Meta omite el id, no como forma de "pisar" la identidad de otra app. Acá Meta nunca muestra
        // NUESTRA propia suscripción (ni antes ni después de reparar): debe intentar reparar (POST) y,
        // como sigue sin poder confirmarse, terminar en fail-controlled -- no en éxito silencioso ni
        // en "unparseable" (sí hay evidencia utilizable, sólo que es de otra app).
        var posts = 0;
        var client = Create(new RoutingHandler(r =>
        {
            if (r.RequestUri!.Host == "callback.test")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(r.RequestUri.Query.Split("hub.challenge=", StringSplitOptions.None)[1].Split('&')[0]) };
            if (r.Method == HttpMethod.Post) { posts++; return Json("{\"success\":true}"); }
            return Json("{\"data\":[{\"id\":\"777\",\"override_callback_uri\":\"https://callback.test/webhook/token\"}]}");
        }));

        var error = await Assert.ThrowsAsync<MetaWhatsAppManagementException>(
            () => client.EnsureWabaSubscriptionAsync("9101", 1, new("ref")));

        Assert.Equal("META_CALLBACK_ROUTING_MISMATCH", error.ErrorCode);
        // No se consideró confirmado (el único id explícito es de otra app): debió intentar reparar.
        Assert.True(posts >= 1);
    }

    [Fact]
    public async Task SubscribedApps_MissingIdWithWrongCallback_RepairsThenConfirmsViaCallbackOnRetry()
    {
        // Primer GET: id ausente y callback distinto al esperado -> no concluyente para confirmar.
        // Repara con POST (override_callback_uri+verify_token) y el GET posterior ya trae el callback
        // correcto (todavía sin id) -> confirma por la regla de evidencia secundaria, sin lanzar.
        var posts = 0;
        var client = Create(new RoutingHandler(r =>
        {
            if (r.RequestUri!.Host == "callback.test")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(r.RequestUri.Query.Split("hub.challenge=", StringSplitOptions.None)[1].Split('&')[0]) };
            if (r.Method == HttpMethod.Post) { posts++; return Json("{\"success\":true}"); }
            return posts == 0
                ? Json("{\"data\":[{\"override_callback_uri\":\"https://otra.test/webhook\"}]}")
                : Json("{\"data\":[{\"override_callback_uri\":\"https://callback.test/webhook/token\"}]}");
        }));

        await client.EnsureWabaSubscriptionAsync("9101", 1, new("ref"));   // no exception

        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task SubscribedApps_AlphanumericId_FailsControlled()
    {
        var client = Create(new RoutingHandler(r => CallbackOrSubscribedApps(r,
            "{\"id\":\"abc123\",\"override_callback_uri\":\"https://callback.test/webhook/token\"}", 0)));

        var error = await Assert.ThrowsAsync<MetaWhatsAppManagementException>(
            () => client.EnsureWabaSubscriptionAsync("9101", 1, new("ref")));

        Assert.Equal("META_SUBSCRIBED_APPS_UNPARSEABLE", error.ErrorCode);
    }

    [Fact]
    public async Task SubscribedApps_OneMalformedAmongMultiple_IsIgnored_ValidItemDeterminesOutcome()
    {
        // El primer ítem no tiene id utilizable; el segundo SÍ es nuestra app, ya suscripta y con el
        // callback correcto. Debe ignorar el primero y resolver por el segundo sin lanzar.
        var handler = new RoutingHandler(r => CallbackOrSubscribedApps(r,
            "{\"note\":\"sin id\"},{\"id\":\"999\",\"override_callback_uri\":\"https://callback.test/webhook/token\"}", 0));
        var client = Create(handler);

        await client.EnsureWabaSubscriptionAsync("9101", 1, new("ref"));   // no exception
    }

    [Fact]
    public async Task SubscribedApps_AllItemsMalformed_FailsControlled()
    {
        var client = Create(new RoutingHandler(r => CallbackOrSubscribedApps(r,
            "{\"id\":\"abc\"},{\"note\":\"tampoco\"}", 0)));

        var error = await Assert.ThrowsAsync<MetaWhatsAppManagementException>(
            () => client.EnsureWabaSubscriptionAsync("9101", 1, new("ref")));

        Assert.Equal("META_SUBSCRIBED_APPS_UNPARSEABLE", error.ErrorCode);
    }

    [Fact]
    public async Task SubscribedApps_EmptyArray_IsNotAnError_MeansNotSubscribedYet()
    {
        // Un array vacío es un estado legítimo ("todavía nadie suscribió esta WABA"), distinto de
        // "vino con ítems pero ninguno se pudo interpretar". No debe fallar: debe suscribir y, tras
        // el POST, la verificación posterior ya encuentra la suscripción propia con el callback OK.
        var posts = 0;
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "callback.test")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(request.RequestUri.Query.Split("hub.challenge=", StringSplitOptions.None)[1].Split('&')[0]) };
            if (request.Method == HttpMethod.Post) { posts++; return Json("{\"success\":true}"); }
            return posts == 0
                ? Json("{\"data\":[]}")
                : Json("{\"data\":[{\"id\":\"999\",\"override_callback_uri\":\"https://callback.test/webhook/token\"}]}");
        });
        var client = Create(handler);

        await client.EnsureWabaSubscriptionAsync("9101", 1, new("ref"));

        Assert.True(posts >= 1);
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
    public async Task RateLimit80008IsRecoverableAndReadsBusinessUsageEstimate()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"code\":80008}}")
        };
        response.Headers.Add("X-Business-Use-Case-Usage", "{\"app\":[{\"estimated_time_to_regain_access\":900}]}" );
        var client = Create(new RoutingHandler(_ => response));

        var error = await Assert.ThrowsAsync<MetaWhatsAppManagementException>(() => client.DiscoverPhoneNumbersAsync("9101", new("ref")));

        Assert.True(error.IsRateLimit);
        Assert.True(error.IsTransient);
        Assert.True(error.HasBusinessUseCaseUsage);
        Assert.Equal(TimeSpan.FromMinutes(15), error.EstimatedTimeToRegainAccess);
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
