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
    public void OtherGraphErrorCode_ClassifiesGenerically_WithoutInventingAnUnverifiedCause()
    {
        var payload = BuildDeliveryErrorPayload(131026, "Message undeliverable");

        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliveryError("ERROR_ENVIO", payload);

        Assert.NotNull(message);
        Assert.NotEqual("Cuenta de WhatsApp bloqueada por Meta", message!.Title);
        Assert.Equal("131026", message.Code);
        Assert.Equal("Message undeliverable", message.Message);
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
