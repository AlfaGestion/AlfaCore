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
///
/// ACTUALIZADO por "Mejora estados UI media historica WhatsApp": el fallback genérico por tipo
/// ("Video histórico no disponible.", "Sticker no disponible.", etc.) fue reemplazado por
/// GetHistoricalMediaStateText, que distingue el estado real vía ResolveHistoricalMessageVisualState
/// (media_id extraído de PayloadJson con ConversacionesService.TryExtractWhatsAppMediaId). Con
/// PayloadJson vacío (sin media_id) el estado resuelve SinReferenciaRecuperable para cualquier tipo,
/// de ahí que las 5 variantes de abajo converjan al mismo texto uniforme -- ya no hay un texto "bonito"
/// por tipo para este caso, sólo para NoDisponibleMeta (ver WhatsAppHistoricalMediaUiStateTests, que
/// cubre exhaustivamente los 5 estados y no se tocó).
/// </summary>
public sealed class WhatsAppHistoricalMediaFallbackTests
{
    private static ConversacionMensajeDto BuildMediaMessage(string messageType, string texto, bool tieneAdjuntos, string payloadJson = "")
        => new()
        {
            MessageType = messageType,
            Texto = texto,
            TieneAdjuntos = tieneAdjuntos,
            PayloadJson = payloadJson
        };

    [Theory]
    [InlineData("VIDEO", "[video]")]
    [InlineData("IMAGE", "[image]")]
    [InlineData("AUDIO", "[audio]")]
    [InlineData("DOCUMENT", "[document]")]
    [InlineData("STICKER", "[sticker]")]
    public void RawBracketPlaceholder_WithoutAnyAttachmentRowNorMediaId_ShowsSinReferenciaRecuperableText(string messageType, string rawText)
    {
        // Sin media_id (PayloadJson vacío) -- SinReferenciaRecuperable: nunca se le atribuye la
        // pérdida a WhatsApp/Meta, y ya no muestra el "[video]"/"[image]" crudo.
        var message = BuildMediaMessage(messageType, rawText, tieneAdjuntos: false);

        Assert.Equal("No se pudo recuperar este archivo histórico.", Conversaciones.GetMessageDisplayText(message));
        Assert.DoesNotContain("WhatsApp", Conversaciones.GetMessageDisplayText(message), StringComparison.Ordinal);
    }

    [Fact]
    public void RawBracketPlaceholder_WithFilename_AlsoGetsTheFriendlyFallback_NotTheRawText()
    {
        // ExtractIncomingText también produce "[document] nombre.pdf" cuando hay filename pero no
        // caption -- también debe reemplazarse, no sólo la forma sin nombre de archivo.
        var message = BuildMediaMessage("DOCUMENT", "[document] contrato.pdf", tieneAdjuntos: false);

        Assert.Equal("No se pudo recuperar este archivo histórico.", Conversaciones.GetMessageDisplayText(message));
    }

    [Fact]
    public void RawBracketPlaceholder_WithRecoverableMediaId_ShowsPendingText_NeverDefinitivelyUnavailable()
    {
        // media_id recuperable en el PayloadJson persistido (sin fila CONV_ADJUNTOS todavía) --
        // PendienteRecuperacion: nunca debe decir "no disponible", el archivo todavía puede aparecer.
        var message = BuildMediaMessage(
            "IMAGE",
            "[image]",
            tieneAdjuntos: false,
            payloadJson: """
                {
                  "metadata": { "phone_number_id": "111" },
                  "message": { "id": "wamid.x", "type": "image", "image": { "id": "media-recuperable-123" } }
                }
                """);

        var text = Conversaciones.GetMessageDisplayText(message);

        Assert.Equal("Archivo histórico pendiente de recuperación.", text);
        Assert.DoesNotContain("no disponible", text, StringComparison.OrdinalIgnoreCase);
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
