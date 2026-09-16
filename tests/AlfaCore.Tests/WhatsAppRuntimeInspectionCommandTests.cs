using System.Net;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;
using static AlfaCore.Configuration.WhatsAppRuntimeInspectionCommand;

namespace AlfaCore.Tests;

/// <summary>
/// --inspect-whatsapp-runtime: comando one-shot 100% read-only. Igual que
/// WhatsAppPhoneInspectionCommandTests, prueba el núcleo testeable (ExecuteAsync) con dobles de
/// ownership/credencial/Graph y delegados fake para las cuatro lecturas SQL (central + tenant) --
/// nunca toca SQL/Vault/Graph reales. Los extractores puros (ExtractOutboundErrorInfo,
/// BuildWebhookEventSummary) se prueban aparte, directamente con fixtures de texto.
/// </summary>
public sealed class WhatsAppRuntimeInspectionCommandTests
{
    private const int Base84 = 84;
    private const string Waba = "869766812883188";
    private const string Phone = "1233329726536711";
    private const string SecretToken = "secret-access-token-xyz-never-printed";
    private static readonly TenantBaseInfo BaseInfo = new(Base84, "ALFANET", "10.8.0.10", "ALFANET", "tenant-user", "tenant-password-never-printed");

    [Fact]
    public async Task FullSuccess_PrintsAllSections_NeverLeaksTenantCredentials()
    {
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, Base84, DateTime.UtcNow));
        var resolver = new FakeCredentialResolver(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", SecretToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup));
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"platform_type":"CLOUD_API","is_on_biz_app":true,"quality_rating":"GREEN"}""")
        });
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();
        var outboundPayload = """{"Error":"Meta devolvió 401: {\"error\":{\"message\":\"Session expired\",\"type\":\"OAuthException\",\"code\":190}}","Type":"System.Net.Http.HttpRequestException"}""";

        var exitCode = await ExecuteAsync(
            Base84, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com",
            _ => Task.FromResult<TenantBaseInfo?>(BaseInfo),
            (b, p, _) => Task.FromResult<TenantNumeroInfo?>(new TenantNumeroInfo(25, "Alfa Business", true)),
            (b, idNumero, _) => Task.FromResult<TenantOutboundMessageInfo?>(new TenantOutboundMessageInfo(999, "wamid.out1", "ERROR_ENVIO", new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc), outboundPayload)),
            (b, idPlantilla, _) => Task.FromResult<TenantTemplateInfo?>(null),
            (b, p, _) => Task.FromResult<TenantWebhookLogInfo?>(new TenantWebhookLogInfo(new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc), true, """{"MessageCount":1,"StatusCount":0,"EventTypes":[],"ErrorCodes":[]}""", null, CorrelatedToPhoneNumberId: true)),
            messageId: null,
            templateId: null,
            output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("TENANT DATABASE = 10.8.0.10/ALFANET", text, StringComparison.Ordinal);
        Assert.Contains("OWNERSHIP = OK", text, StringComparison.Ordinal);
        Assert.Contains("VAULT_CREDENTIAL = OK", text, StringComparison.Ordinal);
        Assert.Contains("ID_NUMERO = 25", text, StringComparison.Ordinal);
        Assert.Contains("NUMERO_ACTIVO = True", text, StringComparison.Ordinal);
        Assert.Contains("ORIGEN/CLASIFICACION = EmbeddedSignup", text, StringComparison.Ordinal);
        Assert.Contains("LAST_OUTBOUND_MESSAGE_ID = 999", text, StringComparison.Ordinal);
        Assert.Contains("LAST_OUTBOUND_WHATSAPP_ID = wamid.out1", text, StringComparison.Ordinal);
        Assert.Contains("LAST_OUTBOUND_ERROR_CODE = 190", text, StringComparison.Ordinal);
        Assert.Contains("Session expired", text, StringComparison.Ordinal);
        Assert.Contains("LAST_META_WEBHOOK_SUCCESS = True", text, StringComparison.Ordinal);
        Assert.Contains("messages=1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NO CORRELACIONADO", text, StringComparison.Ordinal);
        Assert.Contains("HTTP = 200", text, StringComparison.Ordinal);
        Assert.Contains("platform_type = CLOUD_API", text, StringComparison.Ordinal);

        // Nunca se filtra ni el token de Meta ni la contraseña de la connection string tenant.
        Assert.DoesNotContain(SecretToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-password-never-printed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-user", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BaseDoesNotExist_StopsBeforeOwnership()
    {
        var ownershipStore = new ThrowingOwnershipStore();
        var resolver = new ThrowingCredentialResolver(new InvalidOperationException("No debería invocarse."));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Graph no debería llamarse."));
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await ExecuteAsync(
            999, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com",
            _ => Task.FromResult<TenantBaseInfo?>(null),
            (b, p, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, n, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, t, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, p, _) => throw new InvalidOperationException("No debería llamarse."),
            messageId: null,
            templateId: null,
            output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        Assert.Contains("TENANT DATABASE = ERROR", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTenantOwnership_IsBlockedBeforeVaultAndTenantQueries()
    {
        // El PhoneNumberId pertenece a otra base: debe cortar antes de tocar el Vault y antes de
        // consultar CONV_WHATSAPP_NUMEROS/CONV_MENSAJES/CONV_WEBHOOK_LOG.
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, IdBase: 4264, DateTime.UtcNow));
        var resolver = new ThrowingCredentialResolver(new InvalidOperationException("No debería invocarse: cross-tenant corta antes."));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Graph no debería llamarse."));
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();
        var numeroCalled = false;

        var exitCode = await ExecuteAsync(
            Base84, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com",
            _ => Task.FromResult<TenantBaseInfo?>(BaseInfo),
            (b, p, _) => { numeroCalled = true; return Task.FromResult<TenantNumeroInfo?>(null); },
            (b, n, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, t, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, p, _) => throw new InvalidOperationException("No debería llamarse."),
            messageId: null,
            templateId: null,
            output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        Assert.False(numeroCalled);
        Assert.Contains("cross-tenant", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NumeroNotFoundInTenantDb_StillReportsOutboundAndWebhookAsUnavailable()
    {
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, Base84, DateTime.UtcNow));
        var resolver = new FakeCredentialResolver(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", SecretToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup));
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();
        int? idNumeroPassedToOutbound = -1;

        var exitCode = await ExecuteAsync(
            Base84, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com",
            _ => Task.FromResult<TenantBaseInfo?>(BaseInfo),
            (b, p, _) => Task.FromResult<TenantNumeroInfo?>(null),
            (b, n, _) => { idNumeroPassedToOutbound = n; return Task.FromResult<TenantOutboundMessageInfo?>(null); },
            (b, t, _) => Task.FromResult<TenantTemplateInfo?>(null),
            (b, p, _) => Task.FromResult<TenantWebhookLogInfo?>(null),
            messageId: null,
            templateId: null,
            output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Null(idNumeroPassedToOutbound); // se le pasa null al delegado -- nunca inventa un IdNumero.
        var text = output.ToString();
        Assert.Contains("ID_NUMERO = NO ENCONTRADO", text, StringComparison.Ordinal);
        Assert.Contains("LAST_OUTBOUND_MESSAGE_ID = (sin mensajes SALIENTE", text, StringComparison.Ordinal);
        Assert.Contains("LAST_META_WEBHOOK_TIMESTAMP = (sin filas", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UncorrelatedWebhookLog_IsLabeledExplicitly()
    {
        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(Phone, Waba, Base84, DateTime.UtcNow));
        var resolver = new FakeCredentialResolver(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", SecretToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup));
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await ExecuteAsync(
            Base84, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com",
            _ => Task.FromResult<TenantBaseInfo?>(BaseInfo),
            (b, p, _) => Task.FromResult<TenantNumeroInfo?>(new TenantNumeroInfo(1, "X", true)),
            (b, n, _) => Task.FromResult<TenantOutboundMessageInfo?>(null),
            (b, t, _) => Task.FromResult<TenantTemplateInfo?>(null),
            (b, p, _) => Task.FromResult<TenantWebhookLogInfo?>(new TenantWebhookLogInfo(DateTime.UtcNow, false, "{}", "algún error", CorrelatedToPhoneNumberId: false)),
            messageId: null,
            templateId: null,
            output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("NO CORRELACIONADO", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnershipMissing_NeverReachesTenantQueriesOrGraph()
    {
        var ownershipStore = new FakeOwnershipStore(ownership: null);
        var resolver = new ThrowingCredentialResolver(new InvalidOperationException("No debería invocarse."));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Graph no debería llamarse."));
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await ExecuteAsync(
            Base84, Phone, ownershipStore, resolver, httpClient, "https://graph.facebook.com",
            _ => Task.FromResult<TenantBaseInfo?>(BaseInfo),
            (b, p, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, n, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, t, _) => throw new InvalidOperationException("No debería llamarse."),
            (b, p, _) => throw new InvalidOperationException("No debería llamarse."),
            messageId: null,
            templateId: null,
            output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(handler.Called);
        Assert.Contains("OWNERSHIP = ERROR", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateDiagnostic_PrintsActualSendIdentityAndMetaComparisonWithoutToken()
    {
        const long messageId = 157;
        const long templateId = 3;
        const string metaTemplateId = "3293953324141283";
        const string templateName = "contacto_prueba";
        const string language = "es_AR";
        const string expectedWaba = "888902717349521";
        const string expectedPhone = "1243405415530992";

        var ownershipStore = new FakeOwnershipStore(new WhatsAppPhoneOwnership(expectedPhone, expectedWaba, Base84, DateTime.UtcNow));
        var resolver = new FakeCredentialResolver(new WhatsAppRuntimeCredential(expectedWaba, expectedPhone, "v26.0", SecretToken, WhatsAppRuntimeCredentialOrigin.EmbeddedSignup));
        var handler = new RecordingHandler(request =>
        {
            var uri = request.RequestUri?.ToString() ?? "";
            if (uri.Contains("/message_templates", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {"data":[{"id":"{{metaTemplateId}}","name":"{{templateName}}","language":"{{language}}","status":"APPROVED","category":"MARKETING","components":[{"type":"BODY","text":"Hola, este es un mensaje de prueba."}]}]}
                        """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"platform_type":"CLOUD_API","is_on_biz_app":true,"quality_rating":"GREEN"}""")
            };
        });
        using var httpClient = new HttpClient(handler);
        var output = new StringWriter();

        var exitCode = await ExecuteAsync(
            Base84, expectedPhone, ownershipStore, resolver, httpClient, "https://graph.facebook.com",
            _ => Task.FromResult<TenantBaseInfo?>(BaseInfo),
            (b, p, _) => Task.FromResult<TenantNumeroInfo?>(new TenantNumeroInfo(2, "Alfa Claro 1", true)),
            (b, idNumero, _) => Task.FromResult<TenantOutboundMessageInfo?>(new TenantOutboundMessageInfo(messageId, "", "ERROR_ENVIO", DateTime.UtcNow, """{"Error":"Meta devolvio 400: {\"error\":{\"message\":\"(#132001) Template name does not exist in the translation\",\"code\":132001}}","Type":"System.Net.Http.HttpRequestException"}""")),
            (b, idPlantilla, _) => Task.FromResult<TenantTemplateInfo?>(new TenantTemplateInfo(templateId, templateName, language, "APPROVED", metaTemplateId, expectedWaba, true, "Hola, este es un mensaje de prueba.")),
            (b, p, _) => Task.FromResult<TenantWebhookLogInfo?>(null),
            messageId,
            templateId,
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("SEND_PHONE_NUMBER_ID = " + expectedPhone, text, StringComparison.Ordinal);
        Assert.Contains("SEND_WABA_ID = " + expectedWaba, text, StringComparison.Ordinal);
        Assert.Contains("SEND_TEMPLATE_LOCAL_ID = 3", text, StringComparison.Ordinal);
        Assert.Contains("SEND_META_TEMPLATE_ID = " + metaTemplateId, text, StringComparison.Ordinal);
        Assert.Contains("SEND_TEMPLATE_NAME = contacto_prueba", text, StringComparison.Ordinal);
        Assert.Contains("SEND_LANGUAGE_CODE = es_AR", text, StringComparison.Ordinal);
        Assert.Contains("SEND_COMPONENTS = []", text, StringComparison.Ordinal);
        Assert.Contains("LOCAL_TEMPLATE_ID = 3", text, StringComparison.Ordinal);
        Assert.Contains("LOCAL_META_TEMPLATE_ID = " + metaTemplateId, text, StringComparison.Ordinal);
        Assert.Contains("LOCAL_NAME = contacto_prueba", text, StringComparison.Ordinal);
        Assert.Contains("LOCAL_LANGUAGE = es_AR", text, StringComparison.Ordinal);
        Assert.Contains("META_TEMPLATE_FOUND = True", text, StringComparison.Ordinal);
        Assert.Contains("META_TEMPLATE_FOUND_BY_ID = True", text, StringComparison.Ordinal);
        Assert.Contains("ID_MATCH = True", text, StringComparison.Ordinal);
        Assert.Contains("NAME_MATCH = True", text, StringComparison.Ordinal);
        Assert.Contains("LANGUAGE_MATCH = True", text, StringComparison.Ordinal);
        Assert.Contains("WABA_MATCH = True", text, StringComparison.Ordinal);
        Assert.Contains("EVIDENCE = Local DB y Meta coinciden", text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretToken, text, StringComparison.Ordinal);
    }

    // ---- Extractores puros ------------------------------------------------------------------

    [Fact]
    public void ExtractOutboundErrorInfo_GraphErrorEmbeddedInException_ExtractsRealCodeAndMessage()
    {
        var payload = """{"Error":"Meta devolvió 401: {\"error\":{\"message\":\"Session has expired\",\"type\":\"OAuthException\",\"code\":190}}","Type":"System.Net.Http.HttpRequestException"}""";

        var (code, summary) = ExtractOutboundErrorInfo("ERROR_ENVIO", payload);

        Assert.Equal("190", code);
        Assert.Contains("Session has expired", summary);
        Assert.Contains("[GRAPH]", summary);
    }

    [Fact]
    public void ExtractOutboundErrorInfo_FailureBeforeGraph_LabelsStageWithoutRealCode()
    {
        var payload = """{"Error":"La ventana de WhatsApp está vencida.","Type":"System.InvalidOperationException"}""";

        var (code, summary) = ExtractOutboundErrorInfo("ERROR_ENVIO", payload);

        Assert.Null(code);
        Assert.Contains("[ANTES_DE_GRAPH]", summary);
        Assert.Contains("ventana de WhatsApp", summary);
    }

    [Fact]
    public void ExtractOutboundErrorInfo_NotAnErrorStatus_ReturnsNull()
    {
        var (code, summary) = ExtractOutboundErrorInfo("ENVIADO_META", """{"messages":[{"id":"wamid.ok"}]}""");

        Assert.Null(code);
        Assert.Null(summary);
    }

    [Fact]
    public void ExtractOutboundErrorInfo_EmptyPayload_ReturnsNull()
    {
        var (code, summary) = ExtractOutboundErrorInfo("ERROR_ENVIO", null);

        Assert.Null(code);
        Assert.Null(summary);
    }

    [Fact]
    public void ExtractOutboundErrorInfo_NeverThrowsOnMalformedJson()
    {
        var (code, summary) = ExtractOutboundErrorInfo("ERROR_ENVIO", "esto no es json {{{");

        Assert.Null(code);
        Assert.NotNull(summary);
    }

    [Fact]
    public void BuildWebhookEventSummary_HistoryAndEchoes_ListsEventTypesAndCounts()
    {
        var payload = """{"MessageCount":0,"StatusCount":0,"EventTypes":["history","smb_message_echoes"],"HistoryMessageCount":12,"EchoCount":3,"ErrorCodes":[]}""";

        var summary = BuildWebhookEventSummary(payload);

        Assert.Contains("history", summary);
        Assert.Contains("smb_message_echoes", summary);
    }

    [Fact]
    public void BuildWebhookEventSummary_ErrorCodes_AreIncluded()
    {
        var payload = """{"MessageCount":0,"StatusCount":0,"EventTypes":["history"],"ErrorCodes":[2593109]}""";

        Assert.Contains("2593109", BuildWebhookEventSummary(payload));
    }

    [Fact]
    public void BuildWebhookEventSummary_MalformedJson_NeverThrows()
        => Assert.Equal("(no parseable)", BuildWebhookEventSummary("{not json"));

    [Fact]
    public void IsRequested_MatchesVerbCaseInsensitive()
    {
        Assert.True(WhatsAppRuntimeInspectionCommand.IsRequested(["--inspect-whatsapp-runtime", "--id-base", "84"]));
        Assert.True(WhatsAppRuntimeInspectionCommand.IsRequested(["--INSPECT-WHATSAPP-RUNTIME"]));
        Assert.False(WhatsAppRuntimeInspectionCommand.IsRequested(["--inspect-whatsapp-phone"]));
    }

    private sealed class FakeOwnershipStore(WhatsAppPhoneOwnership? ownership) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Task.FromResult(ownership);
    }

    private sealed class ThrowingOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => throw new InvalidOperationException("No debería llamarse: la base ni existe.");
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => throw new NotSupportedException();
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
