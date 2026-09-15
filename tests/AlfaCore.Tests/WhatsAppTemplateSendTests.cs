using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 1 de la auditoría UX (continuación): envío real de plantillas end-to-end.
/// SendTemplateMessageAsync (ConversacionesService.cs) y su UI (Conversaciones.razor) usan varias
/// dependencias reales (SQL, HttpClientFactory, resolvers de credencial) que hacen inviable un test de
/// integración sin [SqlIntegrationFact]; en su lugar se testea aquí (a) la lógica pura extraída
/// (CountTemplateVariables, ya usada tanto por la validación del servidor como por la del cliente) y
/// (b) el contrato por texto fuente de las partes que sí pueden verificarse así, siguiendo el mismo
/// patrón ya establecido en WhatsAppTenantIsolationTests para ConversacionesService.cs.
/// </summary>
public sealed class WhatsAppTemplateSendTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("Hola {{1}}, tu pedido {{2}} está listo.", 2)]
    [InlineData("Hola, sin variables.", 0)]
    [InlineData("{{1}} {{3}}", 3)] // toma el índice máximo, no la cantidad de placeholders distintos
    [InlineData(null, 0)]
    [InlineData("", 0)]
    public void CountTemplateVariables_MatchesHighestPlaceholderIndex(string? body, int expected)
        => Assert.Equal(expected, ConversacionesService.CountTemplateVariables(body));

    [Fact]
    public void SendTemplateMessageAsync_ValidatesVariableCountBeforeCallingGraph()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<ConversacionPlantillaMessageResultDto> SendTemplateMessageAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró SendTemplateMessageAsync.");

        var graphCallIndex = source.IndexOf("SendTemplateToWhatsAppAsync(config,", methodStart, StringComparison.Ordinal);
        Assert.True(graphCallIndex >= 0, "No se encontró la llamada real a Graph dentro de SendTemplateMessageAsync.");

        var variableGuardIndex = source.IndexOf("requiredVariableCount = CountTemplateVariables(template.CuerpoTexto)", methodStart, StringComparison.Ordinal);
        Assert.True(variableGuardIndex >= 0, "No se encontró la validación de variables requeridas.");

        // La validación tiene que estar ANTES de la llamada a Graph, no después -- si no, un template
        // con variables faltantes llegaría a Meta y el rechazo se vería como un error de Graph genérico
        // en vez de un mensaje claro y accionable antes de gastar la llamada.
        Assert.True(variableGuardIndex < graphCallIndex, "La validación de variables debe ejecutarse antes de llamar a Graph.");

        var throwIndex = source.IndexOf("throw new InvalidOperationException(", variableGuardIndex, StringComparison.Ordinal);
        Assert.True(throwIndex >= 0 && throwIndex < graphCallIndex);
    }

    [Fact]
    public void SendTemplateMessageAsync_OnlyAllowsApprovedTemplates()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<ConversacionPlantillaMessageResultDto> SendTemplateMessageAsync(", StringComparison.Ordinal);
        var graphCallIndex = source.IndexOf("SendTemplateToWhatsAppAsync(config,", methodStart, StringComparison.Ordinal);

        var approvedGuardIndex = source.IndexOf(
            "!string.Equals(template.EstadoMeta, \"APPROVED\", StringComparison.OrdinalIgnoreCase))",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(approvedGuardIndex >= 0, "No se encontró el guard de EstadoMeta=APPROVED.");
        Assert.True(approvedGuardIndex < graphCallIndex, "El guard de APPROVED debe ejecutarse antes de llamar a Graph.");
    }

    [Fact]
    public void SendTemplateMessageAsync_RemoteTemplates_AreScopedToTheConversationWaba()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<ConversacionPlantillaMessageResultDto> SendTemplateMessageAsync(", StringComparison.Ordinal);
        var body = source[methodStart..source.IndexOf("SendTemplateToWhatsAppAsync(config,", methodStart, StringComparison.Ordinal)];

        // Un template EsMetaRemota se resuelve SIEMPRE a través de GetTemplatesForConversationAsync
        // (que ya scopea por el runtime credential de ESTA conversación/número, ver
        // WhatsAppRuntimeCredentialResolver) -- nunca por un IdPlantilla suelto que podría pertenecer a
        // otra WABA. Si no matchea nombre+idioma exactos dentro de esa lista scopeada, se rechaza.
        Assert.Contains("GetTemplatesForConversationAsync(request.IdConversacion, token)", body, StringComparison.Ordinal);
        Assert.Contains("La plantilla ya no está aprobada para la WABA de este número.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateModal_HasIndependentFeedbackState_SeparateFromSharedActionError()
    {
        var source = ReadPageSource();

        // _templateSendFeedback existe y se usa dentro de SendTemplateAsync -- feedback propio del
        // intento, no el _actionError compartido por ~15 acciones no relacionadas de la página.
        Assert.Contains("private AppUiMessage? _templateSendFeedback;", source, StringComparison.Ordinal);

        var methodStart = source.IndexOf("private async Task SendTemplateAsync()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);
        Assert.Contains("_templateSendFeedback = AppUiMessage.InProgress(", methodBody, StringComparison.Ordinal);
        Assert.Contains("_templateSendFeedback = null;", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateModal_DoesNotClassifyBusinessRuleExceptionsAsGenericGraphErrors()
    {
        var source = ReadPageSource();
        var methodStart = source.IndexOf("private async Task SendTemplateAsync()", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        // Una InvalidOperationException propia (plantilla no aprobada, variables faltantes, WABA
        // incorrecta) ya trae un mensaje claro y seguro -- no debe forzarse por el clasificador de
        // errores de Graph, que asumiría "Meta no pudo entregar este mensaje" y perdería la causa real.
        Assert.Contains("causeException is InvalidOperationException businessRule", methodBody, StringComparison.Ordinal);
        Assert.Contains("WhatsAppOutboundErrorClassifier.ClassifyDeliverySendException(causeException)", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SendTemplateAsync_GuardsAgainstDoubleSubmit()
    {
        var source = ReadPageSource();
        var methodStart = source.IndexOf("private async Task SendTemplateAsync()", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        // Guard de reentrancia explícito (además del disabled del botón, que depende de un re-render
        // que puede no haber llegado aún al cliente): un segundo click mientras _sendingTemplate ya es
        // true vuelve inmediatamente sin reenviar.
        Assert.Contains("|| _sendingTemplate)", methodBody, StringComparison.Ordinal);
        Assert.Contains("return;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_sendingTemplate = true;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_sendingTemplate = false;", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void CloseTemplatePanel_NeverClosesWhileSendInFlight()
    {
        var source = ReadPageSource();
        var methodStart = source.IndexOf("private void CloseTemplatePanel()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("if (_sendingTemplate)", methodBody, StringComparison.Ordinal);
        Assert.Contains("return;", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void CanSendTemplate_BlocksUnapprovedTemplates_AsDefenseInDepth()
    {
        var source = ReadPageSource();
        var propertyStart = source.IndexOf("private bool CanSendTemplate", StringComparison.Ordinal);
        Assert.True(propertyStart >= 0);
        var propertyBody = source[propertyStart..source.IndexOf(';', propertyStart)];

        Assert.Contains("!IsSelectedTemplateUnavailable", propertyBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedAttentionWindow_HidesFreeTextComposer_AndOffersTemplateCta()
    {
        var source = ReadPageSource();

        // Ya resuelto: cuando la ventana de 24hs está vencida, el composer de texto libre ni siquiera
        // se renderiza (en vez de quedar habilitado y fallar sin explicación) y se muestra un banner
        // con CTA directo a "Enviar plantilla". No se reescribe -- sólo se documenta con este test.
        Assert.Contains("@if (!IsWhatsAppWindowExpired)", source, StringComparison.Ordinal);
        Assert.Contains("Esta conversación superó la ventana de 24 horas de WhatsApp.", source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"OpenTemplatePanel\" disabled=\"@(!CanOpenTemplatePanel)\"", source, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, int methodStart)
    {
        var openBrace = source.IndexOf('{', methodStart);
        Assert.True(openBrace >= 0);
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[methodStart..(i + 1)];
            }
        }
        throw new InvalidOperationException("No se pudo delimitar el cuerpo del método.");
    }

    private static string ReadServiceSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

    private static string ReadPageSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se pudo ubicar la raíz del repositorio (AlfaCore.sln) desde el directorio de pruebas.");
    }
}
