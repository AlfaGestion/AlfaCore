using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Fundación extensible de tipos de mensaje WhatsApp. Auditado contra main (c0eceb21): no existía
/// ningún payload real, fixture ni test previo para mensajes editados ni para visualización única
/// (view-once) -- ni en el Cloud API de Meta ni en el canal WhatsApp Web/Baileys (que de hecho ya
/// descarta esos eventos por completo antes de persistirlos, ver IsWhatsAppWebNonConversationalEvent).
/// Por eso esta fundación NO inventa el nombre del campo real de view-once ni el shape de un mensaje
/// editado de Meta -- construye el clasificador central (WhatsAppMessageClassifier) y deja
/// EsVisualizacionUnica listo para activarse con una condición extra el día que se confirme el campo
/// real, probando mientras tanto el comportamiento consumidor con clasificaciones construidas
/// directamente (no vía Classify).
/// </summary>
public sealed class WhatsAppMessageClassificationTests
{
    // --- Tipos ya soportados: sin regresión ---------------------------------------------------

    [Fact]
    public void TextoNormal_ClasificaComoTexto_NoEsDesconocido()
    {
        var classification = WhatsAppMessageClassifier.Classify("TEXT", origen: "", payloadJson: "");

        Assert.Equal(WhatsAppMessageKind.Texto, classification.Kind);
        Assert.False(classification.EsDesconocido);
        Assert.False(classification.EsMedia);
    }

    [Theory]
    [InlineData("IMAGE")]
    [InlineData("AUDIO")]
    [InlineData("VIDEO")]
    [InlineData("DOCUMENT")]
    [InlineData("STICKER")]
    public void MediaNormal_SigueSiendoMediaRecuperable(string tipo)
    {
        var classification = WhatsAppMessageClassifier.Classify(tipo, origen: "", payloadJson: "");

        Assert.Equal(WhatsAppMessageKind.Media, classification.Kind);
        Assert.True(classification.EsMedia);
        Assert.True(classification.PuedeRecuperarseMedia);
        Assert.False(classification.EsDesconocido);
    }

    [Fact]
    public void Reaction_Location_Contact_Order_Sistema_ClasificanCorrectamente()
    {
        Assert.Equal(WhatsAppMessageKind.Reaccion, WhatsAppMessageClassifier.Classify("REACTION", "", "").Kind);
        Assert.Equal(WhatsAppMessageKind.Ubicacion, WhatsAppMessageClassifier.Classify("LOCATION", "", "").Kind);
        Assert.Equal(WhatsAppMessageKind.Contacto, WhatsAppMessageClassifier.Classify("CONTACT", "", "").Kind);
        Assert.Equal(WhatsAppMessageKind.Contacto, WhatsAppMessageClassifier.Classify("CONTACTS", "", "").Kind);
        Assert.Equal(WhatsAppMessageKind.Pedido, WhatsAppMessageClassifier.Classify("ORDER", "", "").Kind);

        var sistema = WhatsAppMessageClassifier.Classify("SYSTEM", "", "");
        Assert.Equal(WhatsAppMessageKind.Sistema, sistema.Kind);
        Assert.True(sistema.EsSistema);
        Assert.False(sistema.EsDesconocido);
    }

    // --- Unknown / tipos futuros: obligatorio, nunca debe romper --------------------------------

    [Fact]
    public void TipoDesconocido_Clasifica_PeroConservaElTipoOriginalDelPayload()
    {
        var payload = """
            {
              "metadata": { "phone_number_id": "111" },
              "message": { "id": "wamid.x", "type": "edited_message" }
            }
            """;

        var classification = WhatsAppMessageClassifier.Classify("edited_message", origen: "", payloadJson: payload);

        Assert.Equal(WhatsAppMessageKind.Desconocido, classification.Kind);
        Assert.True(classification.EsDesconocido);
        Assert.Equal("edited_message", classification.TipoOriginal);
        Assert.False(classification.EsMedia);
        Assert.False(classification.PuedeRecuperarseMedia);
    }

    [Fact]
    public void TipoDesconocido_UI_NoMuestraCorcheteRawNiFraseDeErrorDelSistema()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "edited_message",
            Texto = "[edited_message]",
            TieneAdjuntos = false,
            PayloadJson = """{"metadata":{},"message":{"id":"wamid.x","type":"edited_message"}}"""
        };

        var text = Conversaciones.GetMessageDisplayText(message);

        Assert.DoesNotContain("[edited_message]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("formato no compatible", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AlfaCore todavía no puede mostrar", text, StringComparison.Ordinal);
        Assert.Contains("Tipo: edited_message", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TipoDesconocidoSinTypeEnPayload_UsaFallbackGenerico_SinTipo()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "algo_totalmente_nuevo",
            Texto = "[algo_totalmente_nuevo]",
            TieneAdjuntos = false,
            PayloadJson = string.Empty
        };

        var text = Conversaciones.GetMessageDisplayText(message);

        Assert.Equal("WhatsApp envió un tipo de mensaje que AlfaCore todavía no puede mostrar.", text);
    }

    [Fact]
    public void Fallback_NuncaExponePayloadJsonNiToken()
    {
        var payloadConDatosSensibles = """
            {
              "metadata": { "phone_number_id": "111" },
              "message": {
                "id": "wamid.x",
                "type": "<script>alert(1)</script> AccessToken=EAABsecret123 https://signed.example/media?token=abcxyz"
              }
            }
            """;

        var message = new ConversacionMensajeDto
        {
            MessageType = "unknown_future_type",
            Texto = "[unknown_future_type]",
            TieneAdjuntos = false,
            PayloadJson = payloadConDatosSensibles
        };

        var text = Conversaciones.GetMessageDisplayText(message);

        Assert.DoesNotContain("AccessToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", text, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone_number_id", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistoryConTipoDesconocido_ClasificaIgualQueOrdinary_NoRompe()
    {
        var classification = WhatsAppMessageClassifier.Classify(
            "future_meta_type",
            origen: "HISTORY",
            payloadJson: """{"metadata":{},"message":{"id":"wamid.h","type":"future_meta_type"}}""");

        Assert.Equal(WhatsAppMessageKind.Desconocido, classification.Kind);
        Assert.Equal("future_meta_type", classification.TipoOriginal);
    }

    [Fact]
    public void EchoConTipoDesconocido_DetectaOrigenBusiness_SinDependerDeDirection()
    {
        var classification = WhatsAppMessageClassifier.Classify(
            "future_meta_type",
            origen: "WHATSAPP_BUSINESS_APP",
            payloadJson: """{"metadata":{},"message":{"id":"wamid.e","type":"future_meta_type"}}""");

        Assert.True(classification.EsOrigenBusiness);
        Assert.Equal(WhatsAppMessageKind.Desconocido, classification.Kind);
    }

    // --- View-once: arquitectura lista, probada con clasificación construida directamente -----

    [Fact]
    public void ViewOnce_MuestraTextoEspecifico_DistintoDeNoDisponibleMetaYDeSinReferencia()
    {
        var message = new ConversacionMensajeDto { MessageType = "IMAGE", Texto = "", TieneAdjuntos = false, PayloadJson = "" };
        var viewOnceClassification = new WhatsAppMessageClassification(
            Kind: WhatsAppMessageKind.Media,
            TipoOriginal: "image",
            EsMedia: true,
            EsSistema: false,
            EsOrigenBusiness: false,
            EsDesconocido: false,
            EsVisualizacionUnica: true,
            PuedeRecuperarseMedia: false);

        var text = Conversaciones.GetMessageDisplayText(message, viewOnceClassification);

        Assert.Equal(Conversaciones.ViewOnceMessageText, text);
        Assert.NotEqual("Este archivo ya no está disponible en WhatsApp.", text);
        Assert.NotEqual("No se pudo recuperar este archivo histórico.", text);
        Assert.DoesNotContain("no disponible", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewOnce_ConMediaIdRealEnPayload_NuncaResuelvePendienteDeRecuperacion()
    {
        // Un mensaje view-once puede traer un media_id real y técnicamente descargable -- WhatsApp
        // igual prohíbe volver a bajarlo. 0 llamadas de recovery: nunca debe dar PendienteRecuperacion.
        var message = new ConversacionMensajeDto
        {
            MessageType = "IMAGE",
            Texto = "[image]",
            TieneAdjuntos = false,
            PayloadJson = """
                {
                  "metadata": { "phone_number_id": "111" },
                  "message": { "id": "wamid.vo", "type": "image", "image": { "id": "media-view-once-123" } }
                }
                """
        };
        var viewOnceClassification = new WhatsAppMessageClassification(
            Kind: WhatsAppMessageKind.Media,
            TipoOriginal: "image",
            EsMedia: true,
            EsSistema: false,
            EsOrigenBusiness: false,
            EsDesconocido: false,
            EsVisualizacionUnica: true,
            PuedeRecuperarseMedia: false);

        var state = Conversaciones.ResolveHistoricalMessageVisualState(message, viewOnceClassification);

        Assert.NotEqual(Conversaciones.ConversacionMediaRecoveryVisualState.PendienteRecuperacion, state);
    }

    [Fact]
    public void SinViewOnce_MismoMediaId_SiResuelvePendienteDeRecuperacion()
    {
        // Control: sin el flag view-once, el mismo media_id sí habilita "pendiente".
        var message = new ConversacionMensajeDto
        {
            MessageType = "IMAGE",
            Texto = "[image]",
            TieneAdjuntos = false,
            PayloadJson = """
                {
                  "metadata": { "phone_number_id": "111" },
                  "message": { "id": "wamid.normal", "type": "image", "image": { "id": "media-normal-123" } }
                }
                """
        };
        var normalClassification = WhatsAppMessageClassifier.Classify(message.MessageType, message.Origen, message.PayloadJson);

        var state = Conversaciones.ResolveHistoricalMessageVisualState(message, normalClassification);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.PendienteRecuperacion, state);
    }

    // --- Sanitización del tipo mostrado ---------------------------------------------------------

    [Fact]
    public void ClassifyHoy_NuncaProduceVisualizacionUnica_HastaConfirmarElCampoReal()
    {
        // Documenta explícitamente la limitación actual -- si algún día Classify empieza a detectar
        // view-once real, este test debe actualizarse junto con la implementación real.
        foreach (var tipo in new[] { "image", "video", "audio", "document", "sticker" })
        {
            var classification = WhatsAppMessageClassifier.Classify(tipo, origen: "", payloadJson: "");
            Assert.False(classification.EsVisualizacionUnica);
        }
    }
}
