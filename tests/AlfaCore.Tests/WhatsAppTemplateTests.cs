using System.Net;
using System.Reflection;
using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppTemplateTests
{
    [Theory]
    [InlineData("A", "B")]
    [InlineData(null, "B")]
    [InlineData("A", null)]
    public void TemplateFromAnotherWabaIsRejected(string? owner, string? requested)
        => Assert.Throws<UnauthorizedAccessException>(() => WhatsAppTemplateValidation.EnsureScope(new() { WabaId = owner }, requested));

    [Theory]
    [InlineData(null)]
    [InlineData("A")]
    public void SameScopeIsAccepted(string? waba)
        => WhatsAppTemplateValidation.EnsureScope(new() { WabaId = waba }, waba);

    [Fact]
    public void EmptyParametersArePreservedAndRejectedWithoutShifting()
    {
        var method = typeof(ConversacionesService).GetMethod("NormalizeTemplateValues", BindingFlags.NonPublic | BindingFlags.Static)!;
        var values = (List<string>)method.Invoke(null, new object[] { new[] { " first ", "", "third" } })!;
        Assert.Equal(new[] { "first", "", "third" }, values);
        Assert.Throws<InvalidOperationException>(() => WhatsAppTemplateValidation.ValidateSend(Template("Hola {{1}} {{2}} {{3}}."), values));
    }

    [Theory]
    [InlineData("Hola {{2}}.", 2)]
    [InlineData("Hola {{0}}.", 1)]
    [InlineData("Hola {{1}}.", 0)]
    [InlineData("Hola {{1}}.", 2)]
    [InlineData("Hola.", 1)]
    public void IncorrectParameterCountOrSequenceIsRejected(string body, int count)
        => Assert.Throws<InvalidOperationException>(() => WhatsAppTemplateValidation.ValidateSend(Template(body), Enumerable.Repeat("value", count).ToArray()));

    [Theory]
    [InlineData("[{\"type\":\"HEADER\",\"format\":\"IMAGE\"}]")]
    [InlineData("[{\"type\":\"HEADER\",\"format\":\"TEXT\",\"text\":\"Hola {{1}}\"}]")]
    [InlineData("[{\"type\":\"BUTTONS\",\"buttons\":[{\"type\":\"URL\",\"url\":\"https://example.test/{{1}}\"}]}]")]
    [InlineData("[{\"type\":\"BUTTONS\",\"buttons\":[{\"type\":\"QUICK_REPLY\",\"text\":\"Si\"}]}]")]
    public void UnsupportedComponentsAreNotSilentlySentAsBodyOnly(string components)
    {
        var template = Template("Hola.");
        template.ComponentesMetaJson = components;
        Assert.Throws<InvalidOperationException>(() => WhatsAppTemplateValidation.ValidateSend(template, []));
    }

    [Theory]
    [InlineData("es_AR", "phone-A")]
    [InlineData("en_US", "phone-B")]
    public async Task SendUsesExactPhoneTokenLanguageAndParameterOrder(string language, string phone)
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal($"/v26.0/{phone}/messages", request.RequestUri!.AbsolutePath);
            Assert.Equal("token-" + phone, request.Headers.Authorization!.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("whatsapp", json.RootElement.GetProperty("messaging_product").GetString());
            Assert.Equal("template", json.RootElement.GetProperty("type").GetString());
            var template = json.RootElement.GetProperty("template");
            Assert.Equal("saludo", template.GetProperty("name").GetString());
            Assert.Equal(language, template.GetProperty("language").GetProperty("code").GetString());
            Assert.Equal(new[] { "Ana", "Pedido 42" }, template.GetProperty("components")[0].GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("text").GetString()));
            return Json("{\"messages\":[{\"id\":\"wamid.fake\"}]}");
        });
        var template = Template("Hola {{1}}, pedido {{2}}.");
        template.Idioma = language;
        var result = await Invoke(Create(handler), "SendTemplateToWhatsAppAsync", Config(phone), "recipient", template, new[] { "Ana", "Pedido 42" }, CancellationToken.None);
        Assert.Equal("ENVIADO_META", Property(result, "EstadoEnvio"));
    }

    [Fact]
    public async Task NoParametersSendsNoBodyComponent()
    {
        var handler = new Handler(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Empty(json.RootElement.GetProperty("template").GetProperty("components").EnumerateArray());
            return Json("{\"messages\":[{\"id\":\"wamid.fake\"}]}");
        });
        await Invoke(Create(handler), "SendTemplateToWhatsAppAsync", Config(), "recipient", Template("Hola."), Array.Empty<string>(), CancellationToken.None);
    }

    [Theory]
    [InlineData("APPROVED")]
    [InlineData("PENDING")]
    [InlineData("REJECTED")]
    [InlineData("PAUSED")]
    [InlineData("DISABLED")]
    [InlineData("IN_APPEAL")]
    public async Task StatusSyncFindsExactNameAndLanguageOnSecondPage(string status)
    {
        var calls = 0;
        var handler = new Handler(request =>
        {
            Assert.Equal("/v26.0/waba-A/message_templates", request.RequestUri!.AbsolutePath);
            Assert.Equal("token-phone-A", request.Headers.Authorization!.Parameter);
            return Task.FromResult(++calls == 1
                ? Json("{\"data\":[{\"id\":\"1\",\"name\":\"saludo\",\"language\":\"en_US\",\"status\":\"APPROVED\"}],\"paging\":{\"next\":\"https://graph.facebook.com/v26.0/waba-A/message_templates?after=next\"}}")
                : Json($"{{\"data\":[{{\"id\":\"2\",\"name\":\"saludo\",\"language\":\"es_AR\",\"status\":\"{status}\"}}]}}"));
        });
        var result = await Invoke(Create(handler), "GetMetaTemplateStatusAsync", Config(), Template("Hola."), CancellationToken.None);
        Assert.Equal(status, Property(result, "EstadoMeta"));
        Assert.Equal("2", Property(result, "MetaTemplateId"));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("https://evil.test/templates")]
    [InlineData("https://graph.facebook.com/v26.0/waba-B/message_templates")]
    public async Task SyncRejectsPaginationOutsideWabaBeforeSendingCredential(string next)
    {
        var calls = 0;
        var service = Create(new Handler(_ => { calls++; return Task.FromResult(Json(JsonSerializer.Serialize(new { data = Array.Empty<object>(), paging = new { next } }))); }));
        await Assert.ThrowsAsync<HttpRequestException>(() => Invoke(service, "GetMetaTemplateStatusAsync", Config(), Template("Hola."), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MetaSendErrorCannotReturnSuccess()
    {
        var service = Create(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"code\":132000,\"fbtrace_id\":\"trace-test\"}}") })));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Invoke(service, "SendTemplateToWhatsAppAsync", Config(), "recipient", Template("Hola."), Array.Empty<string>(), CancellationToken.None));
        Assert.Contains("132000", WhatsAppTemplateValidation.MetaErrorMessage(error));
        Assert.Contains("trace-test", WhatsAppTemplateValidation.MetaErrorMessage(error));
    }

    [Fact]
    public async Task ConversationPhoneAOverridesGlobalPhoneB()
    {
        var configService = DispatchProxy.Create<IConversacionesConfigService, NumberConfigProxy>();
        ((NumberConfigProxy)(object)configService).Number = new() { IdNumero = 7, Activo = true, PhoneNumberId = "phone-A" };
        var constructor = typeof(ConversacionesService).GetConstructors().Single();
        var service = (ConversacionesService)constructor.Invoke(constructor.GetParameters().Select(p => p.ParameterType == typeof(IConversacionesConfigService) ? (object)configService : null).ToArray());
        var identityType = typeof(ConversacionesService).GetNestedType("ConversationIdentity", BindingFlags.NonPublic)!;
        var conversation = Activator.CreateInstance(identityType)!;
        identityType.GetProperty("IdNumeroWhatsApp")!.SetValue(conversation, 7);
        identityType.GetProperty("PhoneNumberId")!.SetValue(conversation, "phone-A");
        var result = await Invoke(service, "ResolveTemplateConversationPhoneAsync", conversation, Config("phone-B"), CancellationToken.None);
        Assert.Equal("phone-A", result);
        ((NumberConfigProxy)(object)configService).Number = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(service, "ResolveTemplateConversationPhoneAsync", conversation, Config("phone-B"), CancellationToken.None));
    }

    [Fact]
    public async Task TemplateConversationPhoneMismatchRejectsStalePhoneWithoutGlobalFallback()
    {
        var configService = DispatchProxy.Create<IConversacionesConfigService, NumberConfigProxy>();
        var numberProxy = (NumberConfigProxy)(object)configService;
        numberProxy.Number = new() { IdNumero = 7, Activo = true, PhoneNumberId = "phone-A" };
        var httpRequests = 0;
        var factory = new Factory(new Handler(_ =>
        {
            httpRequests++;
            throw new InvalidOperationException("No debe enviarse ningún request HTTP ante un mismatch.");
        }));
        var constructor = typeof(ConversacionesService).GetConstructors().Single();
        var service = (ConversacionesService)constructor.Invoke(constructor.GetParameters().Select(p =>
            p.ParameterType == typeof(IConversacionesConfigService) ? (object)configService :
            p.ParameterType == typeof(IHttpClientFactory) ? factory : null).ToArray());
        var identityType = typeof(ConversacionesService).GetNestedType("ConversationIdentity", BindingFlags.NonPublic)!;
        var conversation = Activator.CreateInstance(identityType)!;
        identityType.GetProperty("IdNumeroWhatsApp")!.SetValue(conversation, 7);
        identityType.GetProperty("PhoneNumberId")!.SetValue(conversation, "phone-STALE");
        var global = Config("phone-GLOBAL");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Invoke(service, "ResolveTemplateConversationPhoneAsync", conversation, global, CancellationToken.None));

        Assert.Contains("Phone Number ID", error.Message);
        Assert.Contains("no coincide", error.Message);
        Assert.Equal(7, numberProxy.RequestedNumberId);
        Assert.Equal("phone-GLOBAL", global.PhoneNumberId);
        Assert.Equal("phone-STALE", identityType.GetProperty("PhoneNumberId")!.GetValue(conversation));
        Assert.Equal(0, httpRequests);
    }

    public class NumberConfigProxy : DispatchProxy
    {
        public ConversacionWhatsAppNumeroDto? Number { get; set; }
        public int? RequestedNumberId { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name != "GetWhatsAppNumeroAsync") throw new NotSupportedException();
            RequestedNumberId = (int)args![0]!;
            return Task.FromResult(Number);
        }
    }

    [Fact]
    public async Task CreateWithoutIdCannotReturnSubmittedState()
    {
        var service = Create(new Handler(_ => Task.FromResult(Json("{\"status\":\"PENDING\"}"))));
        await Assert.ThrowsAsync<HttpRequestException>(() => Invoke(service, "CreateMetaTemplateAsync", Config(), Template("Hola."), CancellationToken.None));
    }

    private static ConversacionPlantillaDto Template(string body) => new() { NombreMeta = "saludo", Idioma = "es_AR", CuerpoTexto = body, EstadoMeta = "APPROVED" };
    private static ConversacionWhatsAppConfigDto Config(string phone = "phone-A") => new() { PhoneNumberId = phone, AccessToken = "token-" + phone, BusinessAccountId = "waba-A", ApiVersion = "v26.0" };
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static string? Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)?.ToString();
    private static ConversacionesService Create(Handler handler)
    {
        var constructor = typeof(ConversacionesService).GetConstructors().Single();
        return (ConversacionesService)constructor.Invoke(constructor.GetParameters().Select(p => p.ParameterType == typeof(IHttpClientFactory) ? (object)new Factory(handler) : null).ToArray());
    }
    private static async Task<object> Invoke(ConversacionesService service, string method, params object[] arguments)
    {
        var task = (Task)typeof(ConversacionesService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, arguments)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request);
    }
}
