using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppHistoricalMediaUiStateTests
{
    [Fact]
    public void ContenidoDisponible_ResuelveDisponible()
    {
        var state = Conversaciones.ResolveMediaRecoveryVisualState(
            archivoDisponible: true,
            estadoAlmacenamiento: string.Empty,
            tieneMediaIdRecuperable: false);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.Disponible, state);
    }

    [Fact]
    public void NoDisponibleMeta_ResuelveEstadoDefinitivo()
    {
        var state = Conversaciones.ResolveMediaRecoveryVisualState(
            archivoDisponible: false,
            estadoAlmacenamiento: "NO_DISPONIBLE_META",
            tieneMediaIdRecuperable: false);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.NoDisponibleMeta, state);
    }

    [Fact]
    public void NoDisponibleMeta_EsDefinitivoAunqueHubieraMediaIdRecuperable()
    {
        // Si Meta ya confirmó (NO_DISPONIBLE_META), no importa que la fila todavía conserve un
        // media_id en su PayloadJson -- sigue siendo definitivo, nunca "pendiente".
        var state = Conversaciones.ResolveMediaRecoveryVisualState(
            archivoDisponible: false,
            estadoAlmacenamiento: "no_disponible_meta",
            tieneMediaIdRecuperable: true);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.NoDisponibleMeta, state);
    }

    [Fact]
    public void MediaIdSinContenido_ResuelvePendienteDeRecuperacion()
    {
        var state = Conversaciones.ResolveMediaRecoveryVisualState(
            archivoDisponible: false,
            estadoAlmacenamiento: string.Empty,
            tieneMediaIdRecuperable: true);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.PendienteRecuperacion, state);
    }

    [Fact]
    public void FalloLegacy_NoEsDefinitivo()
    {
        // RECUPERACION_FALLIDA es el estado legacy que el backend pre-hardening escribía incluso ante
        // fallas transitorias (timeouts, 5xx) -- nunca debe leerse como pérdida definitiva.
        var state = Conversaciones.ResolveMediaRecoveryVisualState(
            archivoDisponible: false,
            estadoAlmacenamiento: "RECUPERACION_FALLIDA",
            tieneMediaIdRecuperable: false);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.FalloTemporal, state);
        Assert.NotEqual(Conversaciones.ConversacionMediaRecoveryVisualState.NoDisponibleMeta, state);
    }

    [Fact]
    public void SinMediaIdNiEstado_ResuelveSinReferenciaRecuperable()
    {
        var state = Conversaciones.ResolveMediaRecoveryVisualState(
            archivoDisponible: false,
            estadoAlmacenamiento: string.Empty,
            tieneMediaIdRecuperable: false);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.SinReferenciaRecuperable, state);
    }

    [Fact]
    public void SoloEstadoDefinitivoMencionaWhatsApp()
    {
        var casos = new (Conversaciones.ConversacionMediaRecoveryVisualState State, bool DebeDecirNoDisponibleEnWhatsApp)[]
        {
            (Conversaciones.ConversacionMediaRecoveryVisualState.NoDisponibleMeta, true),
            (Conversaciones.ConversacionMediaRecoveryVisualState.PendienteRecuperacion, false),
            (Conversaciones.ConversacionMediaRecoveryVisualState.FalloTemporal, false),
            (Conversaciones.ConversacionMediaRecoveryVisualState.SinReferenciaRecuperable, false)
        };

        foreach (var caso in casos)
        {
            var text = Conversaciones.GetHistoricalMediaStateText(caso.State, "imagen");
            Assert.Equal(
                caso.DebeDecirNoDisponibleEnWhatsApp,
                text.Contains("no está disponible en WhatsApp", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("imagen", "Esta imagen ya no está disponible en WhatsApp.")]
    [InlineData("audio", "Este audio ya no está disponible en WhatsApp.")]
    [InlineData("video", "Este video ya no está disponible en WhatsApp.")]
    [InlineData("documento", "Este documento ya no está disponible en WhatsApp.")]
    public void NoDisponibleMeta_AdaptaElTextoAlTipo(string tipoLabel, string esperado)
    {
        var text = Conversaciones.GetHistoricalMediaStateText(
            Conversaciones.ConversacionMediaRecoveryVisualState.NoDisponibleMeta,
            tipoLabel);

        Assert.Equal(esperado, text);
    }

    [Fact]
    public void TextosNoExponenEstadosTecnicos()
    {
        foreach (var state in Enum.GetValues<Conversaciones.ConversacionMediaRecoveryVisualState>())
        {
            var text = Conversaciones.GetHistoricalMediaStateText(state, "imagen");

            Assert.DoesNotContain("NO_DISPONIBLE_META", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RECUPERACION_FALLIDA", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("media_id", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MensajeSinAdjunto_ConMediaIdEnPayload_NoDiceNoDisponible()
    {
        var message = new ConversacionMensajeDto
        {
            TieneAdjuntos = false,
            MessageType = "IMAGE",
            Texto = "[image]",
            PayloadJson = """
                {
                  "metadata": { "phone_number_id": "111" },
                  "message": { "id": "wamid.x", "type": "image", "image": { "id": "media-abc" } }
                }
                """
        };

        var state = Conversaciones.ResolveHistoricalMessageVisualState(message);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.PendienteRecuperacion, state);
    }

    [Fact]
    public void MensajeSinAdjunto_SinMediaIdEnPayload_ResuelveSinReferencia()
    {
        var message = new ConversacionMensajeDto
        {
            TieneAdjuntos = false,
            MessageType = "IMAGE",
            Texto = "[image]",
            PayloadJson = """
                {
                  "metadata": { "phone_number_id": "111" },
                  "message": { "id": "wamid.x", "type": "text", "text": { "body": "hola" } }
                }
                """
        };

        var state = Conversaciones.ResolveHistoricalMessageVisualState(message);

        Assert.Equal(Conversaciones.ConversacionMediaRecoveryVisualState.SinReferenciaRecuperable, state);
    }

    [Fact]
    public void ShouldRecoverConversationAttachments_DisparaPorMediaIdSinImportarAntiguedad()
    {
        var mensajeViejoConMediaId = new ConversacionMensajeDto
        {
            IdMensaje = 1,
            TieneAdjuntos = false,
            MessageType = "IMAGE",
            Texto = "[image]",
            FechaHora = DateTime.Now.AddDays(-45),
            PayloadJson = """
                {
                  "metadata": { "phone_number_id": "111" },
                  "message": { "id": "wamid.viejo", "type": "image", "image": { "id": "media-viejo-123" } }
                }
                """
        };

        var result = Conversaciones.ShouldRecoverConversationAttachments(
            [mensajeViejoConMediaId],
            []);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRecoverConversationAttachments_NoDisparaSinMediaId()
    {
        var mensajeViejoSinMediaId = new ConversacionMensajeDto
        {
            IdMensaje = 2,
            TieneAdjuntos = false,
            MessageType = "IMAGE",
            Texto = "[image]",
            FechaHora = DateTime.Now.AddDays(-45),
            PayloadJson = """
                {
                  "metadata": { "phone_number_id": "111" },
                  "message": { "id": "wamid.sinmedia", "type": "text", "text": { "body": "hola" } }
                }
                """
        };

        var result = Conversaciones.ShouldRecoverConversationAttachments(
            [mensajeViejoSinMediaId],
            []);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRecoverConversationAttachments_NoDisparaParaAdjuntoNoDisponibleMeta()
    {
        // El mensaje ya tiene fila CONV_ADJUNTOS (TieneAdjuntos=true) -- rama de mensaje sin adjunto no
        // aplica. La rama de adjuntos existentes usa PuedeRecuperarse, que el backend ya calcula en
        // false para NO_DISPONIBLE_META (ver IsAttachmentRecoveryCandidate) -- nunca queda reintentando
        // en loop un adjunto que Meta ya confirmó perdido.
        var mensajeConAdjuntoConfirmadoPerdido = new ConversacionMensajeDto
        {
            IdMensaje = 3,
            TieneAdjuntos = true,
            MessageType = "IMAGE",
            Texto = string.Empty,
            FechaHora = DateTime.Now.AddDays(-1)
        };
        var adjuntoNoDisponibleMeta = new ConversacionAdjuntoDto
        {
            IdMensaje = 3,
            TipoArchivo = "IMAGE",
            ArchivoDisponible = false,
            PuedeRecuperarse = false,
            EstadoAlmacenamiento = "NO_DISPONIBLE_META"
        };

        var result = Conversaciones.ShouldRecoverConversationAttachments(
            [mensajeConAdjuntoConfirmadoPerdido],
            [adjuntoNoDisponibleMeta]);

        Assert.False(result);
    }
}
