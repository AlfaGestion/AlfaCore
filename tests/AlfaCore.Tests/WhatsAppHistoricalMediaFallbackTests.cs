using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 5/6 de la auditoría UX: bug real reportado -- historicamente, un mensaje de video (por
/// ejemplo) aparecía literalmente como "[video]" en la burbuja, en vez de una explicación. Trazado por
/// código (no adivinado): ExtractIncomingText (ConversacionesService) ya usaba "[{type}]" como el
/// fallback final cuando un mensaje de media no tiene caption ni filename -- ese texto crudo queda
/// guardado en CONV_MENSAJES.Texto. Cuando el mensaje SÍ tiene un adjunto registrado,
/// HasHumanAttachmentText ya lo filtraba (StartsWith("[")) y no se mostraba. El gap real era cuando NO
/// hay NINGÚN adjunto registrado (típico de historial cuyo media Meta ya no sirve) -- ahí
/// ShouldShowMessageText igual devolvía true (el texto no está vacío) y se mostraba el "[video]" crudo.
///
/// Auditoría de CSS de renderizado normal (.message-attachment--image img,
/// .message-attachment--video video, tarjeta de documento, fallback "no disponible" por adjunto) ya
/// tenía max-width/max-height/object-fit correctos y estados de "no disponible" con ícono+texto -- no
/// se encontró evidencia de la queja "imágenes enormes" en el código actual, así que no se tocó esa
/// parte (podría ya estar resuelta, o requerir reproducción visual que este test no puede cubrir).
/// </summary>
public sealed class WhatsAppHistoricalMediaFallbackTests
{
    private static ConversacionMensajeDto BuildMediaMessage(string messageType, string texto, bool tieneAdjuntos)
        => new()
        {
            MessageType = messageType,
            Texto = texto,
            TieneAdjuntos = tieneAdjuntos,
            PayloadJson = string.Empty
        };

    [Theory]
    [InlineData("VIDEO", "[video]", "Video histórico no disponible.")]
    [InlineData("IMAGE", "[image]", "Imagen histórica no disponible.")]
    [InlineData("AUDIO", "[audio]", "Audio histórico no disponible.")]
    [InlineData("DOCUMENT", "[document]", "Documento histórico no disponible.")]
    [InlineData("STICKER", "[sticker]", "Sticker no disponible.")]
    public void RawBracketPlaceholder_WithoutAnyAttachmentRow_ShowsFriendlyUnavailableText(string messageType, string rawText, string expected)
    {
        var message = BuildMediaMessage(messageType, rawText, tieneAdjuntos: false);

        Assert.Equal(expected, Conversaciones.GetMessageDisplayText(message));
    }

    [Fact]
    public void RawBracketPlaceholder_WithFilename_AlsoGetsTheFriendlyFallback_NotTheRawText()
    {
        // ExtractIncomingText también produce "[document] nombre.pdf" cuando hay filename pero no
        // caption -- también debe reemplazarse, no sólo la forma sin nombre de archivo.
        var message = BuildMediaMessage("DOCUMENT", "[document] contrato.pdf", tieneAdjuntos: false);

        Assert.Equal("Documento histórico no disponible.", Conversaciones.GetMessageDisplayText(message));
    }

    [Fact]
    public void MediaMessage_WithAttachmentRow_KeepsItsOwnText_NeverOverriddenByTheFallback()
    {
        // Con TieneAdjuntos=true, el renderer real de adjuntos (message-attachments) es responsable de
        // la representación -- GetMessageDisplayText no debe pisarlo con el fallback de "no disponible".
        var message = BuildMediaMessage("VIDEO", "[video]", tieneAdjuntos: true);

        Assert.Equal("[video]", Conversaciones.GetMessageDisplayText(message));
    }

    [Fact]
    public void NonMediaMessageType_NeverGetsTheMediaFallback_EvenWithBracketLikeText()
    {
        var message = BuildMediaMessage("TEXT", "[not really media]", tieneAdjuntos: false);

        Assert.Equal("[not really media]", Conversaciones.GetMessageDisplayText(message));
    }

    [Fact]
    public void MediaMessage_WithNormalCaptionText_IsNeverTreatedAsUnavailable()
    {
        // Sólo el patrón "[...]" activa el fallback -- un caption real de usuario nunca debe perderse.
        var message = BuildMediaMessage("IMAGE", "Mirá esta foto del pedido", tieneAdjuntos: false);

        Assert.Equal("Mirá esta foto del pedido", Conversaciones.GetMessageDisplayText(message));
    }
}
