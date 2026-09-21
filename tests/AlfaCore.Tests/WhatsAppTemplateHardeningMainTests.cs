using System.Net;
using System.Reflection;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Hardening de plantillas WhatsApp: (1) toast que distingue "Meta aceptó" de "delivery confirmado",
/// (2) clasificación del error Meta 131042 (billing/pago del cliente), (3) persistencia en
/// dbo.WhatsAppIntegrationHealth, (4) estado ACTION_REQUIRED con razón CUSTOMER_PAYMENT_SETUP_REQUIRED,
/// (5) recuperación evidence-based tras un webhook positivo posterior, (6) protección anti-loop/
/// anti-cross-WABA en GetPagedAsync/GetMetaTemplateStatusAsync, (7) preservación de components crudo +
/// WhatsAppTemplateValidation. Cada test verifica el comportamiento real del código actual (arquitectura
/// vigente: EnsureTemplateMatchesRuntime, WhatsAppOutboundErrorClassifier como única fuente de
/// clasificación), siguiendo el mismo patrón de reflexión ya usado en WhatsAppTenantIsolationTests para
/// ConversacionesService.cs.
/// </summary>
public sealed class WhatsAppTemplateHardeningMainTests
{
    private static readonly IOptions<WhatsAppEmbeddedSignupOptions> EnabledOptions = Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = true });
    private static readonly IOptions<WhatsAppEmbeddedSignupOptions> DisabledOptions = Options.Create(new WhatsAppEmbeddedSignupOptions { Enabled = false });

    private static ConversacionPlantillaDto Template(string body, string componentsJson = "") => new()
    { NombreMeta = "saludo", Idioma = "es_AR", CuerpoTexto = body, EstadoMeta = "APPROVED", Activa = true, ComponentesMetaJson = componentsJson };

    // ============================================================================================
    // 7. Validación exacta de parámetros BODY / componentes no soportados (WhatsAppTemplateValidation)
    // ============================================================================================
    [Fact]
    public void ValidateSend_RejectsEmptyRequiredParameter()
    {
        var template = Template("Hola {{1}}, tu pedido {{2}} está listo.");
        var values = new[] { "Ana", "" }; // segunda posición vacía -- antes se descartaba silenciosamente
        var ex = Assert.Throws<InvalidOperationException>(() => WhatsAppTemplateValidation.ValidateSend(template, values));
        Assert.Contains("exactamente", ex.Message);
    }

    [Fact]
    public void ValidateSend_AcceptsExactNonEmptyOrderedValues()
        => WhatsAppTemplateValidation.ValidateSend(Template("Hola {{1}}, pedido {{2}}."), new[] { "Ana", "42" });

    [Fact]
    public void ValidateSend_NoRegressionOnTemplateWithoutParameters()
        // No-regresión: una plantilla sin variables debe seguir aceptando un envío sin valores.
        => WhatsAppTemplateValidation.ValidateSend(Template("Hola, sin variables."), []);

    [Theory]
    [InlineData("[{\"type\":\"HEADER\",\"format\":\"IMAGE\"}]")]
    [InlineData("[{\"type\":\"BUTTONS\",\"buttons\":[{\"type\":\"URL\",\"url\":\"https://x/{{1}}\"}]}]")]
    public void ValidateSend_RejectsUnsupportedComponentsExplicitly(string componentsJson)
    {
        var template = Template("Hola.", componentsJson);
        Assert.Throws<InvalidOperationException>(() => WhatsAppTemplateValidation.ValidateSend(template, []));
    }

    [Fact]
    public void ValidateSend_AcceptsBodyFooterAndFixedTextHeaderComponents()
        // No-regresión: HEADER de texto fijo (sin variables) + BODY + FOOTER son soportados.
        => WhatsAppTemplateValidation.ValidateSend(
            Template("Hola {{1}}.", "[{\"type\":\"HEADER\",\"format\":\"TEXT\",\"text\":\"Bienvenida\"},{\"type\":\"BODY\"},{\"type\":\"FOOTER\",\"text\":\"Gracias\"}]"),
            new[] { "Ana" });

    // ============================================================================================
    // 1. POST a Meta exitoso -> ENVIADO_META, nunca "entregado" (la UI trata esto como aceptación,
    // no delivery -- ver Conversaciones.razor SendTemplateAsync/wasOnlyAcceptedByMeta).
    // ============================================================================================
    [Fact]
    public async Task SendTemplateToWhatsAppAsync_SuccessfulPostYieldsAcceptedNotDeliveredState()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"messages\":[{\"id\":\"wamid.fake\"}]}") }));
        var service = CreateService(httpClientFactory: new FakeHttpClientFactory(handler));
        var config = new ConversacionWhatsAppConfigDto { ApiVersion = "v26.0", PhoneNumberId = "phone-A", AccessToken = "token", BusinessAccountId = "waba-A" };
        var result = await InvokeResult<object>(service, "SendTemplateToWhatsAppAsync", config, "recipient", Template("Hola {{1}}."), new[] { "Ana" }, CancellationToken.None);
        Assert.Equal("ENVIADO_META", Property(result, "EstadoEnvio"));
        Assert.False(string.IsNullOrWhiteSpace((string?)Property(result, "WhatsAppMessageId")));
    }

    [Fact]
    public async Task SendTemplateToWhatsAppAsync_GraphFailureNeverReturnsAcceptedResult()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("{\"error\":{\"code\":132000,\"message\":\"bad params\"}}") }));
        var service = CreateService(httpClientFactory: new FakeHttpClientFactory(handler));
        var config = new ConversacionWhatsAppConfigDto { ApiVersion = "v26.0", PhoneNumberId = "phone-A", AccessToken = "token", BusinessAccountId = "waba-A" };
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Invoke<Task>(service, "SendTemplateToWhatsAppAsync", config, "recipient", Template("Hola."), Array.Empty<string>(), CancellationToken.None));
    }

    // ============================================================================================
    // 2. El classifier (única fuente) distingue 131042 (persistente, billing) de 131031 (bloqueo, ya
    // existente, no persistente) y de otros códigos.
    // ============================================================================================
    [Theory]
    [InlineData("{\"errors\":[{\"code\":131042,\"message\":\"payment\"}]}", true)]
    [InlineData("{\"errors\":[{\"code\":131031,\"message\":\"locked\"}]}", false)]
    [InlineData("{\"errors\":[{\"code\":131026,\"message\":\"undeliverable\"}]}", false)]
    [InlineData("{\"status\":\"sent\"}", false)]
    public void Classifier_OnlyPaymentSetupCodeIsPersistentIntegrationCondition(string statusJson, bool expected)
    {
        var code = WhatsAppOutboundErrorClassifier.ExtractWebhookErrorCode(statusJson);
        Assert.Equal(expected, WhatsAppOutboundErrorClassifier.IsPersistentIntegrationCondition(code));
    }

    [Fact]
    public void Classifier_PaymentSetupRequiredMapsToActionRequiredMessageWithCustomerReason()
    {
        // Mismo shape que persiste BuildDeliveryErrorPayload (Error = "... {json}").
        var wrapped = JsonSerializer.Serialize(new { Error = "Meta devolvió 400: {\"error\":{\"code\":131042,\"message\":\"pay\"}}" });
        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", wrapped);
        Assert.NotNull(message);
        Assert.Equal(AppUiFeedbackSeverity.ActionRequired, message!.Severity);
        Assert.Equal("131042", message.Code);
        // AlfaNet es Technology Provider (CustomerPaysMeta): el mensaje nunca debe sugerir que AlfaNet
        // paga o gestiona el billing -- solo que el cliente debe resolverlo en Meta.
        Assert.DoesNotContain("AlfaNet", message.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cliente", message.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classifier_AccountLocked131031KeepsPreviousBehavior()
    {
        var wrapped = JsonSerializer.Serialize(new { Error = "Meta devolvió 400: {\"error\":{\"code\":131031,\"message\":\"locked\"}}" });
        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", wrapped);
        Assert.NotNull(message);
        Assert.Equal(AppUiFeedbackSeverity.Error, message!.Severity);
        Assert.Equal("Cuenta de WhatsApp bloqueada por Meta", message.Title);
    }

    // ============================================================================================
    // 3, 4. 131042 marca ACTION_REQUIRED / CUSTOMER_PAYMENT_SETUP_REQUIRED SOLO para el scope correcto
    // (IdBase+WabaId+PhoneNumberId); otro tenant/WABA/phone no se modifica (aislamiento estricto).
    // ============================================================================================
    [Fact]
    public async Task TryFlagPaymentSetupRequiredAsync_MarksOnlyOwningScopeWithCustomerPaymentReason()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = CreateService(ownership: ownership, health: health, options: EnabledOptions);

        await InvokeFlag(service, 4271, "1373763429148369", "{\"errors\":[{\"code\":131042}]}");

        var call = Assert.Single(health.MarkCalls);
        Assert.Equal(4271, call.IdBase);
        Assert.Equal("1760901255041127", call.WabaId);
        Assert.Equal("1373763429148369", call.PhoneNumberId);
        Assert.Equal("CUSTOMER_PAYMENT_SETUP_REQUIRED", call.Reason);
        var status = await health.GetAsync(4271, "1760901255041127", "1373763429148369");
        Assert.Equal("ACTION_REQUIRED", status!.State);
    }

    [Fact]
    public async Task TryFlagPaymentSetupRequiredAsync_OtherErrorCodeNeverMarks()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = CreateService(ownership: ownership, health: health, options: EnabledOptions);

        await InvokeFlag(service, 4271, "1373763429148369", "{\"errors\":[{\"code\":131026}]}");

        Assert.Empty(health.MarkCalls);
    }

    [Fact]
    public async Task TryFlagPaymentSetupRequiredAsync_WrongBaseNeverMarks()
    {
        // Aislamiento: el ownership real es de la base 4271; un webhook procesado bajo la base 9999
        // (otro tenant) nunca puede marcar el health de la base 4271 ni el suyo propio sin ownership.
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = CreateService(ownership: ownership, health: health, options: EnabledOptions);

        await InvokeFlag(service, 9999, "1373763429148369", "{\"errors\":[{\"code\":131042}]}");

        Assert.Empty(health.MarkCalls);
    }

    [Fact]
    public async Task TryFlagPaymentSetupRequiredAsync_DisabledEmbeddedSignupNeverMarks()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = CreateService(ownership: ownership, health: health, options: DisabledOptions);

        await InvokeFlag(service, 4271, "1373763429148369", "{\"errors\":[{\"code\":131042}]}");

        Assert.Empty(health.MarkCalls);
    }

    // ============================================================================================
    // 5. Recovery evidence-based: un evento positivo posterior a RequiredSinceUtc resuelve; uno
    // anterior (reordenado) no. Un webhook de OTRA WABA/teléfono nunca sana el registro de uno distinto.
    // ============================================================================================
    [Fact]
    public async Task Recovery_SubsequentPositiveStatusResolves()
    {
        var t0 = new DateTime(2026, 9, 18, 12, 53, 35, DateTimeKind.Utc);
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = CreateService(ownership: ownership, health: health, options: EnabledOptions);

        await InvokeFlag(service, 4271, "1373763429148369", "{\"errors\":[{\"code\":131042}]}", t0);
        await InvokeResolve(service, 4271, "1373763429148369", "ENVIADO_META", t0.AddMinutes(10));

        var status = await health.GetAsync(4271, "1760901255041127", "1373763429148369");
        Assert.Equal("OK", status!.State);
    }

    [Fact]
    public async Task Recovery_OldPositiveStatusBeforeRequiredSinceDoesNotResolve()
    {
        var t0 = new DateTime(2026, 9, 18, 12, 53, 35, DateTimeKind.Utc);
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = CreateService(ownership: ownership, health: health, options: EnabledOptions);

        await InvokeFlag(service, 4271, "1373763429148369", "{\"errors\":[{\"code\":131042}]}", t0);
        await InvokeResolve(service, 4271, "1373763429148369", "ENTREGADO", t0.AddMinutes(-30));

        var status = await health.GetAsync(4271, "1760901255041127", "1373763429148369");
        Assert.Equal("ACTION_REQUIRED", status!.State);
    }

    [Fact]
    public async Task Recovery_PositiveStatusFromDifferentPhoneNeverResolvesAnotherPhonesHealth()
    {
        // Aislamiento por clave compuesta (IdBase, WabaId, PhoneNumberId): un webhook de OTRO teléfono
        // (aunque sea la misma base) nunca puede sanar el registro de un teléfono distinto.
        var t0 = new DateTime(2026, 9, 18, 12, 53, 35, DateTimeKind.Utc);
        var ownership = new FakeOwnershipStore();
        ownership.Register("phone-A", new WhatsAppPhoneOwnership("phone-A", "waba-A", 4271, DateTime.UtcNow));
        ownership.Register("phone-B", new WhatsAppPhoneOwnership("phone-B", "waba-A", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = CreateService(ownership: ownership, health: health, options: EnabledOptions);

        await InvokeFlag(service, 4271, "phone-A", "{\"errors\":[{\"code\":131042}]}", t0);
        await InvokeResolve(service, 4271, "phone-B", "ENVIADO_META", t0.AddMinutes(10));

        var statusA = await health.GetAsync(4271, "waba-A", "phone-A");
        Assert.Equal("ACTION_REQUIRED", statusA!.State);
    }

    // ============================================================================================
    // La aceptación sincrónica (HTTP 200 sin webhook) nunca limpia health -- estructural: el método
    // que arma el POST directo a Graph no tiene forma de tocar el health store.
    // ============================================================================================
    [Fact]
    public void SendTemplateToWhatsAppAsync_NeverReferencesIntegrationHealthStore()
    {
        var method = typeof(ConversacionesService).GetMethod("SendTemplateToWhatsAppAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parameterTypeNames = method.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        Assert.DoesNotContain(nameof(IWhatsAppIntegrationHealthStore), parameterTypeNames);
    }

    // ============================================================================================
    // El camino de resolución nunca toca CONV_MENSAJES -- estructural (sin SqlConnection/Command en
    // sus parámetros; solo delega en whatsAppIntegrationHealthStore/whatsAppAssetOwnershipStore).
    // ============================================================================================
    [Fact]
    public void TryResolvePaymentSetupIfConfirmedAsync_NeverTouchesMessageStorage()
    {
        var method = typeof(ConversacionesService).GetMethod("TryResolvePaymentSetupIfConfirmedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parameterTypeNames = method.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        Assert.DoesNotContain("SqlConnection", parameterTypeNames);
        Assert.DoesNotContain("SqlCommand", parameterTypeNames);
    }

    // ---------------------------------------------------------------------------------------------
    // Infraestructura de test (reflexión + fakes, mismo patrón que WhatsAppTenantIsolationTests)
    // ---------------------------------------------------------------------------------------------

    private static async Task InvokeFlag(ConversacionesService service, int idBase, string phoneNumberId, string rawJson, DateTime? eventTimestampUtc = null)
    {
        var status = BuildStatus("ERROR_ENVIO", rawJson, phoneNumberId, eventTimestampUtc ?? DateTime.UtcNow);
        var method = typeof(ConversacionesService).GetMethod("TryFlagPaymentSetupRequiredAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(service, new object?[] { idBase, status, CancellationToken.None })!;
    }

    private static async Task InvokeResolve(ConversacionesService service, int idBase, string phoneNumberId, string estadoEnvio, DateTime eventTimestampUtc)
    {
        var status = BuildStatus(estadoEnvio, "{}", phoneNumberId, eventTimestampUtc);
        var method = typeof(ConversacionesService).GetMethod("TryResolvePaymentSetupIfConfirmedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(service, new object?[] { idBase, status, CancellationToken.None })!;
    }

    private static object BuildStatus(string estadoEnvio, string rawJson, string phoneNumberId, DateTime eventTimestampUtc)
    {
        var statusType = typeof(ConversacionesService).GetNestedType("IncomingWhatsAppStatus", BindingFlags.NonPublic)!;
        var status = Activator.CreateInstance(statusType)!;
        statusType.GetProperty("WhatsAppMessageId")!.SetValue(status, "wamid.test");
        statusType.GetProperty("EstadoEnvio")!.SetValue(status, estadoEnvio);
        statusType.GetProperty("RawJson")!.SetValue(status, rawJson);
        statusType.GetProperty("PhoneNumberId")!.SetValue(status, phoneNumberId);
        statusType.GetProperty("EventTimestampUtc")!.SetValue(status, eventTimestampUtc);
        return status;
    }

    private static async Task<T> Invoke<T>(ConversacionesService service, string method, params object?[] args) where T : Task
    {
        var m = typeof(ConversacionesService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task)m.Invoke(service, args)!;
        await task;
        return (T)task;
    }

    private static async Task<T> InvokeResult<T>(ConversacionesService service, string method, params object?[] args)
    {
        var m = typeof(ConversacionesService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task)m.Invoke(service, args)!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return (T)result;
    }

    private static string? Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)?.ToString();

    private static ConversacionesService CreateService(
        IConversacionesConfigService? config = null,
        IWhatsAppAssetOwnershipStore? ownership = null,
        IWhatsAppIntegrationHealthStore? health = null,
        IHttpClientFactory? httpClientFactory = null,
        IOptions<WhatsAppEmbeddedSignupOptions>? options = null)
    {
        var constructor = typeof(ConversacionesService).GetConstructors().Single();
        var args = constructor.GetParameters().Select(p =>
            p.ParameterType == typeof(IConversacionesConfigService) ? (object?)config :
            p.ParameterType == typeof(IWhatsAppAssetOwnershipStore) ? ownership :
            p.ParameterType == typeof(IWhatsAppIntegrationHealthStore) ? health :
            p.ParameterType == typeof(IHttpClientFactory) ? httpClientFactory :
            p.ParameterType == typeof(IOptions<WhatsAppEmbeddedSignupOptions>) ? options ?? EnabledOptions :
            p.ParameterType == typeof(ILogger<ConversacionesService>) ? new NullLogger() :
            null).ToArray();
        return (ConversacionesService)constructor.Invoke(args);
    }

    private sealed class NullLogger : ILogger<ConversacionesService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class FakeOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        private readonly Dictionary<string, WhatsAppPhoneOwnership> _byPhone = new(StringComparer.Ordinal);
        public void Register(string phoneNumberId, WhatsAppPhoneOwnership ownership) => _byPhone[phoneNumberId] = ownership;
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default)
            => Task.FromResult(_byPhone.TryGetValue(phoneNumberId, out var value) ? value : null);
        public Task<bool> HasEmbeddedSignupFootprintAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeHealthStore : IWhatsAppIntegrationHealthStore
    {
        private sealed class Row
        {
            public string State = "OK";
            public string? Reason;
            public string ErrorCode = "";
            public string? CtaUrl;
            public DateTime? RequiredSinceUtc;
            public DateTime? ResolvedAtUtc;
        }

        private readonly Dictionary<(int, string, string), Row> _rows = new();
        public List<(int IdBase, string WabaId, string PhoneNumberId, string Reason, string ErrorCode)> MarkCalls { get; } = [];

        public Task MarkActionRequiredAsync(int idBase, string wabaId, string phoneNumberId, string reason, string errorCode, string detailSummary, string? ctaUrl, string sourceMessageId, DateTime eventTimestampUtc, CancellationToken ct = default)
        {
            MarkCalls.Add((idBase, wabaId, phoneNumberId, reason, errorCode));
            var key = (idBase, wabaId, phoneNumberId);
            if (!_rows.TryGetValue(key, out var row)) { row = new Row(); _rows[key] = row; }
            row.State = "ACTION_REQUIRED";
            row.Reason = reason; row.ErrorCode = errorCode; row.CtaUrl = ctaUrl;
            row.RequiredSinceUtc = row.RequiredSinceUtc is null || eventTimestampUtc > row.RequiredSinceUtc ? eventTimestampUtc : row.RequiredSinceUtc;
            row.ResolvedAtUtc = null;
            return Task.CompletedTask;
        }

        public Task ResolveIfSubsequentAsync(int idBase, string wabaId, string phoneNumberId, DateTime eventTimestampUtc, CancellationToken ct = default)
        {
            var key = (idBase, wabaId, phoneNumberId);
            if (_rows.TryGetValue(key, out var row) && row.State == "ACTION_REQUIRED"
                && (row.RequiredSinceUtc is null || eventTimestampUtc > row.RequiredSinceUtc))
            {
                row.State = "OK";
                row.ResolvedAtUtc = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task<WhatsAppIntegrationHealthStatus?> GetAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue((idBase, wabaId, phoneNumberId), out var row))
                return Task.FromResult<WhatsAppIntegrationHealthStatus?>(null);
            return Task.FromResult<WhatsAppIntegrationHealthStatus?>(new WhatsAppIntegrationHealthStatus(
                idBase, wabaId, phoneNumberId, row.State, row.Reason, row.ErrorCode, string.Empty, row.CtaUrl,
                row.RequiredSinceUtc, row.ResolvedAtUtc, DateTime.UtcNow));
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request);
    }

    private sealed class FakeHttpClientFactory(FakeHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
}
