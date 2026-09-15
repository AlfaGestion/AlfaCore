using System.Text.Json;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Fase 3 de la auditoría UX: caso real de producción (Base84) donde el Graph GET de inspección
/// devolvió 200 con quality_rating=GREEN/platform_type=CLOUD_API/is_on_biz_app=true simultáneamente
/// mientras la cuenta estaba bloqueada por Meta (error 131031, "Business Account locked"). Antes de
/// este cambio, un envío fallido por 131031 se mostraba igual que cualquier otro ERROR_ENVIO genérico
/// -- el usuario no tenía forma de distinguir "cuenta bloqueada por Meta, no reintentar" de una falla
/// transitoria cualquiera.
/// </summary>
public sealed class WhatsAppOutboundErrorClassifierTests
{
    private static string BuildDeliveryErrorPayload(int graphCode, string graphMessage)
    {
        var graphBody = JsonSerializer.Serialize(new { error = new { code = graphCode, message = graphMessage } });
        return JsonSerializer.Serialize(new
        {
            Error = $"Meta devolvió 400: {graphBody}",
            Type = "System.Net.Http.HttpRequestException",
            FechaHora = DateTime.UtcNow
        });
    }

    [Fact]
    public void AccountLocked131031_ClassifiesAsBigErrorPanel_NeverGenericErrorEnvio()
    {
        var payload = BuildDeliveryErrorPayload(131031, "Business Account locked");

        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", payload);

        Assert.NotNull(message);
        Assert.Equal(AppUiFeedbackSeverity.Error, message!.Severity);
        Assert.Contains("bloqueada", message.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Meta", message.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WhatsAppGraphErrorCodes.AccountLocked, message.Code);
        Assert.True(WhatsAppOutboundErrorClassifier.IsAccountLocked(payload));
    }

    [Fact]
    public void AccountLocked_NeverSuggestsRetryingOrRegeneratingCredentials()
    {
        var payload = BuildDeliveryErrorPayload(131031, "Business Account locked");

        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", payload)!;

        // No debe insinuar que reintentar, reconectar o regenerar el token resuelve un bloqueo de
        // cuenta -- eso sólo se resuelve con Meta (ver Fase 3: no ofboard/regenerar token automático).
        Assert.DoesNotContain("reintent", message.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reconect", message.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Meta", message.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnmappedGraphErrorCode_ClassifiesGenerically_WithoutInventingAnUnverifiedCause()
    {
        var payload = BuildDeliveryErrorPayload(999999, "Some future Meta error nobody mapped yet");

        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", payload);

        Assert.NotNull(message);
        Assert.NotEqual("Cuenta de WhatsApp bloqueada por Meta", message!.Title);
        Assert.Equal("999999", message.Code);
        Assert.Equal("Some future Meta error nobody mapped yet", message.Message);
    }

    [Theory]
    [InlineData(131047, "Ventana de atención vencida")]
    [InlineData(190, "WhatsApp requiere reconexión")]
    [InlineData(131026, "Destinatario inválido")]
    [InlineData(131048, "Envíos limitados temporalmente por Meta")]
    [InlineData(130429, "Envíos limitados temporalmente por Meta")]
    public void WellKnownMetaErrorCodes_ClassifyWithASpecificActionableMessage(int graphCode, string expectedTitle)
    {
        // Prioridad 6: estos son los códigos que Meta documenta públicamente para WhatsApp Cloud API --
        // a diferencia de 131031, ninguno tiene todavía un caso confirmado con evidencia real de
        // producción en AlfaCore (ver comentario en WhatsAppGraphErrorCodes). Igual deben mostrar una
        // causa específica en vez del "Error al enviar" genérico.
        var payload = BuildDeliveryErrorPayload(graphCode, "Mensaje de error crudo de Meta");

        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", payload);

        Assert.NotNull(message);
        Assert.Equal(expectedTitle, message!.Title);
        Assert.NotEqual("Mensaje de error crudo de Meta", message.Message);
    }

    [Fact]
    public void NonErrorState_ReturnsNull_NothingToClassify()
    {
        Assert.Null(WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ENTREGADO", "{}"));
        Assert.Null(WhatsAppOutboundErrorClassifier.ClassifyDeliveryError(null, null));
        Assert.Null(WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("", "some payload"));
    }

    [Fact]
    public void MalformedPayload_NeverThrows_DegradesToNoExtraction()
    {
        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", "{not-json");

        Assert.NotNull(message);
        Assert.Equal("No se pudo enviar", message!.Title);
        Assert.Equal(string.Empty, message.Code);
    }

    [Fact]
    public void PayloadWithoutEmbeddedGraphError_DegradesGracefully()
    {
        var payload = JsonSerializer.Serialize(new
        {
            Error = "System.TimeoutException: la operación superó el tiempo de espera",
            Type = "System.TimeoutException",
            FechaHora = DateTime.UtcNow
        });

        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", payload);

        Assert.NotNull(message);
        Assert.Equal("Meta no pudo entregar este mensaje.", message!.Message);
        Assert.Equal(string.Empty, message.Code);
    }
}
