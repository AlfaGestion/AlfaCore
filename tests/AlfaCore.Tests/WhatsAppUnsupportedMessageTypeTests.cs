using System.Text.Json;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 4 de la auditoría UX: el usuario vio "WhatsApp envió un tipo de mensaje que todavía no es
/// compatible con el módulo" -- demasiado genérico. Auditando el código real (no adivinando tipos) se
/// encontraron dos bugs concretos:
///
/// 1) ExtractUnsupportedType/ExtractUnsupportedReason (Conversaciones.razor) buscaban "unsupported.type"
///    y "errors" en la RAÍZ del PayloadJson, pero BuildIncomingWhatsAppMessagePayloadJson
///    (ConversacionesService, usada tanto para mensajes en vivo como para history/echoes) siempre
///    envuelve el mensaje real de Meta como {metadata:{...}, message:{...}} -- el tipo/errors reales
///    están dentro de "message", no en la raíz. Por eso SIEMPRE caía al fallback genérico, incluso
///    cuando Meta sí mandaba el nombre del tipo o el motivo.
/// 2) NormalizeMessageType (ConversacionesService) no tenía un caso para "order", a pesar de que
///    ExtractIncomingText/ExtractOrderText YA tenían un extractor dedicado ("Pedido de catálogo
///    recibido...") desde antes -- un mensaje de pedido real terminaba clasificado como MessageType=
///    UNKNOWN y la UI descartaba ese texto ya armado para mostrar el genérico "no compatible" en su
///    lugar. No es un tipo inventado: la extracción de texto ya existía, sólo faltaba la clasificación.
/// </summary>
public sealed class WhatsAppUnsupportedMessageTypeTests
{
    private static string BuildWrappedPayload(object message)
        => JsonSerializer.Serialize(new { metadata = new { phone_number_id = "123" }, message });

    [Fact]
    public void ExtractUnsupportedType_ReadsTheRealTypeFromTheMessageWrapper()
    {
        var payload = BuildWrappedPayload(new { type = "order", id = "wamid.1" });

        Assert.Equal("order", Conversaciones.ExtractUnsupportedType(payload));
    }

    [Fact]
    public void ExtractUnsupportedType_ReturnsEmpty_WhenMetaItselfCouldNotIdentifyTheType()
    {
        // type="unsupported" es la etiqueta de Meta para "no sé qué es esto" -- no es un nombre de tipo
        // útil para mostrarle al usuario ("WhatsApp envió un formato no compatible: unsupported." no
        // aporta nada). El motivo real (si vino) lo da ExtractUnsupportedReason.
        var payload = BuildWrappedPayload(new { type = "unsupported", id = "wamid.1" });

        Assert.Equal(string.Empty, Conversaciones.ExtractUnsupportedType(payload));
    }

    [Fact]
    public void ExtractUnsupportedReason_ReadsErrorsFromInsideTheMessageWrapper_NotTheRoot()
    {
        var payload = BuildWrappedPayload(new
        {
            type = "unsupported",
            id = "wamid.1",
            errors = new[]
            {
                new
                {
                    title = "Unsupported message type",
                    message = "Message type is not currently supported",
                    error_data = new { details = "Type not currently supported by this module" }
                }
            }
        });

        var reason = Conversaciones.ExtractUnsupportedReason(payload);
        Assert.Equal("Motivo: Type not currently supported by this module", reason);
    }

    [Fact]
    public void ExtractUnsupportedType_ReturnsEmpty_ForMalformedOrUnwrappedPayload()
    {
        Assert.Equal(string.Empty, Conversaciones.ExtractUnsupportedType(string.Empty));
        Assert.Equal(string.Empty, Conversaciones.ExtractUnsupportedType("{not-json"));
        Assert.Equal(string.Empty, Conversaciones.ExtractUnsupportedType("{}"));
    }

    [Fact]
    public void GetMessageDisplayText_ShowsTheRealTypeName_InsteadOfTheFullyGenericFallback()
    {
        var payload = BuildWrappedPayload(new { type = "order", id = "wamid.1" });
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            Texto = string.Empty,
            PayloadJson = payload
        };

        Assert.Equal("WhatsApp envió un formato no compatible: order.", Conversaciones.GetMessageDisplayText(message));
    }

    [Fact]
    public void GetMessageDisplayText_FallsBackToFullyGenericMessage_OnlyWhenNoTypeIsKnown()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            Texto = string.Empty,
            PayloadJson = string.Empty
        };

        Assert.Equal("WhatsApp envió un tipo de mensaje que todavía no es compatible con el módulo.", Conversaciones.GetMessageDisplayText(message));
    }

    [Theory]
    [InlineData("image", "IMAGE")]
    [InlineData("video", "VIDEO")]
    [InlineData("audio", "AUDIO")]
    [InlineData("document", "DOCUMENT")]
    [InlineData("sticker", "STICKER")]
    [InlineData("location", "LOCATION")]
    [InlineData("contacts", "CONTACT")]
    [InlineData("reaction", "REACTION")]
    [InlineData("system", "SYSTEM")]
    [InlineData("interactive", "TEXT")]
    [InlineData("button", "TEXT")]
    [InlineData("order", "ORDER")]
    [InlineData("some_future_meta_type_nobody_has_seen_yet", "UNKNOWN")]
    public void NormalizeMessageType_ClassifiesEveryRealTypeAlfaCoreParses(string metaType, string expected)
        => Assert.Equal(expected, ConversacionesService.NormalizeMessageType(metaType));

    [Fact]
    public void ExtractIncomingText_OrderMessage_ProducesAFriendlySpanishDescription_NotDiscarded()
    {
        var message = JsonDocument.Parse("""
            {"type":"order","order":{"catalog_id":"c1","product_items":[{"product_retailer_id":"a"},{"product_retailer_id":"b"}]}}
            """).RootElement;

        var text = ConversacionesService.ExtractIncomingText(message, "order");

        Assert.Contains("Pedido de cat", text, StringComparison.Ordinal);
        Assert.Contains("2 producto(s)", text, StringComparison.Ordinal);
    }
}
