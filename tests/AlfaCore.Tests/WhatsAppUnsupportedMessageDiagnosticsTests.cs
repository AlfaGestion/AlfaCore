using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Mini fix sobre la foundation de tipos WhatsApp (a9133409): GetUnsupportedMessageTrace leía
/// "type" en la RAÍZ de PayloadJson (ExtractPayloadString), pero el envelope real es
/// {metadata:{...}, message:{type:...}} -- "Tipo recibido:" quedaba vacío aunque el tipo hubiera
/// llegado. Reusa WhatsAppMessageClassifier.ExtractRawMessageType (mismo parser que ya usa la
/// foundation, sin duplicar lectura de JSON) y agrega el origen (ordinary/history/echo) para poder
/// diagnosticar tipos nuevos (edit, view-once, futuros) sin mirar el PayloadJson crudo en la base.
/// </summary>
public sealed class WhatsAppUnsupportedMessageDiagnosticsTests
{
    private static string BuildWrappedPayload(string type)
        => "{\"metadata\":{\"phone_number_id\":\"111\"},\"message\":{\"id\":\"wamid.x\",\"type\":\"" + type + "\"}}";

    [Fact]
    public void EnvelopeConTypeEdit_DiagnosticoObtieneElTipoReal()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            WhatsAppMessageId = "wamid.123",
            PayloadJson = BuildWrappedPayload("edit")
        };

        var trace = Conversaciones.GetUnsupportedMessageTrace(message);

        Assert.Contains("Tipo recibido: edit", trace, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("unsupported")]
    [InlineData("view_once_media")]
    public void TipoNormalOFuturo_DiagnosticoCorrecto(string type)
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            PayloadJson = BuildWrappedPayload(type)
        };

        var trace = Conversaciones.GetUnsupportedMessageTrace(message);

        Assert.Contains($"Tipo recibido: {type}", trace, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadSinType_NoRompe_NuncaAgregaLineaDeTipo()
    {
        var mensajeVacio = new ConversacionMensajeDto { MessageType = "UNKNOWN", PayloadJson = string.Empty };
        var mensajeSinTypeEnMessage = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            PayloadJson = """{"metadata":{"phone_number_id":"111"},"message":{"id":"wamid.x"}}"""
        };
        var mensajeMalformado = new ConversacionMensajeDto { MessageType = "UNKNOWN", PayloadJson = "{not-json" };

        // Ninguno de los tres tiene "type" utilizable -- no debe romper (ni tirar excepción, ver el
        // llamado en sí, que ya no está en un try/catch propio porque no hace falta) ni inventar un
        // valor: la línea "Tipo recibido:" simplemente no aparece.
        Assert.DoesNotContain("Tipo recibido:", Conversaciones.GetUnsupportedMessageTrace(mensajeVacio), StringComparison.Ordinal);
        Assert.DoesNotContain("Tipo recibido:", Conversaciones.GetUnsupportedMessageTrace(mensajeSinTypeEnMessage), StringComparison.Ordinal);
        Assert.DoesNotContain("Tipo recibido:", Conversaciones.GetUnsupportedMessageTrace(mensajeMalformado), StringComparison.Ordinal);
    }

    [Fact]
    public void MensajeCompletamenteVacio_MuestraElFallbackGenerico()
    {
        // Único caso donde de verdad no hay nada útil para mostrar: Origen inválido (no mapea a
        // ninguna fuente conocida) + sin type + sin motivo + sin WhatsAppMessageId.
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            Origen = "ALGO_NUEVO_NO_MAPEADO",
            PayloadJson = string.Empty
        };

        Assert.Equal("Sin detalle técnico disponible en el webhook.", Conversaciones.GetUnsupportedMessageTrace(message));
    }

    [Fact]
    public void TipoHostilNoSanitizable_NuncaSeExponeEnElDiagnostico()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            PayloadJson = BuildWrappedPayload("<script>alert(1)</script>")
        };

        var trace = Conversaciones.GetUnsupportedMessageTrace(message);

        Assert.DoesNotContain("<script>", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("Tipo recibido:", trace, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadConTokenYUrlFirmada_DiagnosticoNuncaLosIncluye()
    {
        var payloadConDatosSensibles = """
            {
              "metadata": { "phone_number_id": "5511999998888", "display_phone_number": "5511999998888" },
              "message": {
                "id": "wamid.x",
                "type": "image",
                "image": {
                  "id": "media-123",
                  "url": "https://lookaside.fbsbx.com/whatsapp_business/attachments/?mid=abc&access_token=EAABsecretTOKEN123"
                }
              }
            }
            """;
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            PayloadJson = payloadConDatosSensibles
        };

        var trace = Conversaciones.GetUnsupportedMessageTrace(message);

        Assert.DoesNotContain("access_token", trace, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EAAB", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("5511999998888", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("lookaside", trace, StringComparison.OrdinalIgnoreCase);
    }

    // --- Origen (ordinary/history/echo), sin inventar fuentes nuevas ---------------------------

    [Fact]
    public void Origen_OrdinaryHistoryEcho_SeReflejanCorrectamente()
    {
        var ordinary = new ConversacionMensajeDto { MessageType = "UNKNOWN", Origen = "", PayloadJson = BuildWrappedPayload("edit") };
        var history = new ConversacionMensajeDto { MessageType = "UNKNOWN", Origen = "HISTORY", PayloadJson = BuildWrappedPayload("edit") };
        var echo = new ConversacionMensajeDto { MessageType = "UNKNOWN", Origen = "WHATSAPP_BUSINESS_APP", PayloadJson = BuildWrappedPayload("edit") };

        Assert.Equal("ordinary", Conversaciones.GetMessageSourceLabel(ordinary));
        Assert.Equal("history", Conversaciones.GetMessageSourceLabel(history));
        Assert.Equal("echo", Conversaciones.GetMessageSourceLabel(echo));

        Assert.Contains("Origen: ordinary", Conversaciones.GetUnsupportedMessageTrace(ordinary), StringComparison.Ordinal);
        Assert.Contains("Origen: history", Conversaciones.GetUnsupportedMessageTrace(history), StringComparison.Ordinal);
        Assert.Contains("Origen: echo", Conversaciones.GetUnsupportedMessageTrace(echo), StringComparison.Ordinal);
    }

    [Fact]
    public void Origen_ValorNoReconocido_NoInventaEtiqueta()
    {
        var message = new ConversacionMensajeDto { MessageType = "UNKNOWN", Origen = "ALGO_NUEVO_NO_MAPEADO" };

        Assert.Equal(string.Empty, Conversaciones.GetMessageSourceLabel(message));
    }
}
