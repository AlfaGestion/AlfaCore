using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConversacionesCitasTests
{
    private static ConversacionMensajeDto Reply(string target = "wamid.original")
        => new() { IdMensaje = 1000, IdConversacion = 10, Direction = "ENTRANTE", WhatsAppMessageId = "wamid.reply", WhatsAppReplyToMessageId = target, Texto = "A ese precio me refería" };

    private static ConversacionMensajeDto Original(long conversationId = 10)
        => new() { IdMensaje = 1, IdConversacion = conversationId, Direction = "SALIENTE", WhatsAppMessageId = "wamid.original", Texto = "El precio actualizado es 1500", UsuarioAutor = "Marcos", FechaHora = new(2026, 10, 1) };

    [Fact]
    public void CitaFueraDePaginaIncluyeTextoYAutorSinAgregarElOriginalAlTimeline()
    {
        var reply = Reply();
        var page = new List<ConversacionMensajeDto> { reply };
        ConversacionesService.AttachQuotedMessages(10, page, [Original()]);
        Assert.Single(page);
        Assert.Equal(1000, page[0].IdMensaje);
        Assert.Equal("El precio actualizado es 1500", reply.MensajeCitado!.Texto);
        Assert.Equal("Marcos", reply.MensajeCitado.UsuarioAutor);
        Assert.Null(reply.MensajeCitado.MensajeCitado);
    }

    [Fact]
    public void NuncaCargaElOriginalDeOtroHiloConElMismoIdentificador()
    {
        var reply = Reply();
        ConversacionesService.AttachQuotedMessages(10, [reply], [Original(20)]);
        Assert.Null(reply.MensajeCitado);
    }

    [Fact]
    public void IdentificadoresExternosSonSensiblesAMayusculas()
    {
        var reply = Reply("wamid.ORIGINAL");
        ConversacionesService.AttachQuotedMessages(10, [reply], [Original()]);
        Assert.Null(reply.MensajeCitado);
    }

    [Fact]
    public void OriginalAusenteMantieneReferenciaParaMostrarAviso()
    {
        var reply = Reply();
        ConversacionesService.AttachQuotedMessages(10, [reply], []);
        Assert.Null(reply.MensajeCitado);
        Assert.Equal("wamid.original", reply.WhatsAppReplyToMessageId);
    }

    [Fact]
    public void NoCitaNotasInternasNiElPropioMensaje()
    {
        var reply = Reply();
        var note = Original();
        note.Direction = "NOTA_INTERNA";
        ConversacionesService.AttachQuotedMessages(10, [reply], [note]);
        Assert.Null(reply.MensajeCitado);
        var same = Original();
        same.IdMensaje = reply.IdMensaje;
        ConversacionesService.AttachQuotedMessages(10, [reply], [same]);
        Assert.Null(reply.MensajeCitado);
    }

    [Fact]
    public void ConservaReferenciaAImagenSinTexto()
    {
        var reply = Reply();
        var original = Original();
        original.Texto = "";
        original.MessageType = "IMAGE";
        original.TieneAdjuntos = true;
        ConversacionesService.AttachQuotedMessages(10, [reply], [original]);
        Assert.Equal("IMAGE", reply.MensajeCitado!.MessageType);
        Assert.True(reply.MensajeCitado.TieneAdjuntos);
    }

    [Theory]
    [InlineData("""{"message":{"type":"text","context":{"id":"wamid.original"}}}""")]
    [InlineData("""{"type":"text","context":{"id":"wamid.original"}}""")]
    public void RecuperaReferenciaHistoricaDesdeElPayload(string payload)
    {
        var reply = Reply("");
        reply.PayloadJson = payload;
        ConversacionesService.RestoreQuotedMessageReference(reply);
        Assert.Equal("wamid.original", reply.WhatsAppReplyToMessageId);
    }

    [Theory]
    [InlineData("no-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"message":{"context":"invalid"}}""")]
    public void PayloadInvalidoNoRompeLaConversacion(string payload)
    {
        var reply = Reply("");
        reply.PayloadJson = payload;
        ConversacionesService.RestoreQuotedMessageReference(reply);
        Assert.Empty(reply.WhatsAppReplyToMessageId);
    }

    [Fact]
    public void NoSobrescribeUnaReferenciaYaPersistida()
    {
        var reply = Reply("wamid.saved");
        reply.PayloadJson = """{"context":{"id":"wamid.old"}}""";
        ConversacionesService.RestoreQuotedMessageReference(reply);
        Assert.Equal("wamid.saved", reply.WhatsAppReplyToMessageId);
    }
}
