using System.Text.Json;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Mensajes que WhatsApp manda con tipos especiales (2026-10-09): edición, eliminación, lo que solo se
/// ve en el celular (visualización única y formatos no soportados, error 131051) y tipos desconocidos
/// que traen un texto legible (ej. mensajes enviados por empresas). El payload "unsupported" es el real
/// de producción; el de "edit" no está confirmado y se prueba la extracción tolerante.
/// </summary>
public sealed class WhatsAppTiposEspecialesTests
{
    private static JsonElement Mensaje(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void NoSoportado_ConPayloadReal_DiceQueSoloSeVeEnElCelular()
    {
        var message = Mensaje("""
            {"from":"5491135183783","id":"wamid.X","timestamp":"1790795874",
             "errors":[{"code":131051,"title":"Message type unknown","message":"Message type unknown",
                        "error_data":{"details":"Message type is currently not supported."}}],
             "type":"unsupported","unsupported":{"type":"unknown","raw_type":"unknown"}}
            """);

        Assert.Equal(WhatsAppMessageClassifier.SoloEnCelularText, ConversacionesService.ExtractIncomingText(message, "unsupported"));
    }

    [Fact]
    public void ErroresDelHistorial_DiceQueSoloSeVeEnElCelular()
    {
        var message = Mensaje("""{"id":"wamid.X","type":"errors","errors":[{"code":131051}]}""");

        Assert.Equal(WhatsAppMessageClassifier.SoloEnCelularText, ConversacionesService.ExtractIncomingText(message, "errors"));
    }

    [Fact]
    public void Edicion_ConTextoNuevo_LoMuestraComoEditado()
    {
        var message = Mensaje("""
            {"id":"wamid.E","type":"edit","edit":{"original_message_id":"wamid.O","message":{"type":"text","text":{"body":"Precio con uva"}}}}
            """);

        Assert.Equal("✏️ Mensaje editado: Precio con uva", ConversacionesService.ExtractIncomingText(message, "edit"));
        Assert.Equal("wamid.O", WhatsAppMessageClassifier.FindEditOriginalId(message));
    }

    [Fact]
    public void Edicion_SinTexto_NoMuestraElTipoCrudo()
    {
        var message = Mensaje("""{"id":"wamid.E","type":"edit"}""");

        var text = ConversacionesService.ExtractIncomingText(message, "edit");

        Assert.DoesNotContain("[edit]", text, StringComparison.Ordinal);
        Assert.StartsWith("✏️", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Eliminacion_DiceQueSeEliminoUnMensaje()
    {
        var message = Mensaje("""{"id":"wamid.R","type":"revoke","revoke":{"original_message_id":"wamid.O"}}""");

        Assert.Equal(WhatsAppMessageClassifier.MensajeEliminadoText, ConversacionesService.ExtractIncomingText(message, "revoke"));
    }

    [Fact]
    public void TipoDesconocidoConTexto_GuardaElTextoLegible()
    {
        var message = Mensaje("""{"id":"wamid.B","type":"business_notice","business_notice":{"header":{"title":"Banco"},"body":{"text":"Tu resumen está listo"}}}""");

        Assert.Equal("Tu resumen está listo", ConversacionesService.ExtractIncomingText(message, "business_notice"));
    }

    [Fact]
    public void TipoDesconocidoSinTexto_SigueGuardandoElTipo()
    {
        var message = Mensaje("""{"id":"wamid.B","type":"algo_nuevo","algo_nuevo":{"id":"x1"}}""");

        Assert.Equal("[algo_nuevo]", ConversacionesService.ExtractIncomingText(message, "algo_nuevo"));
    }

    [Fact]
    public void UI_MensajeViejoNoSoportado_MuestraTextoDeCelular()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            Texto = "Mensaje no compatible recibido: unknown",
            PayloadJson = """{"metadata":{},"message":{"id":"wamid.X","type":"unsupported","unsupported":{"type":"unknown"}}}"""
        };

        Assert.Equal(WhatsAppMessageClassifier.SoloEnCelularText, Conversaciones.GetMessageDisplayText(message));
    }

    [Fact]
    public void UI_MensajeViejoEditado_NoMuestraFormatoNoCompatible()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            Texto = "[edit]",
            PayloadJson = """{"metadata":{},"message":{"id":"wamid.E","type":"edit"}}"""
        };

        var text = Conversaciones.GetMessageDisplayText(message);

        Assert.StartsWith("✏️", text, StringComparison.Ordinal);
        Assert.DoesNotContain("no puede mostrar", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UI_TipoDesconocidoConTextoGuardado_MuestraEseTexto()
    {
        var message = new ConversacionMensajeDto
        {
            MessageType = "UNKNOWN",
            Texto = "Mensaje recibido en Instagram.",
            PayloadJson = """{"message":{"mid":"m1"}}"""
        };

        Assert.Equal("Mensaje recibido en Instagram.", Conversaciones.GetMessageDisplayText(message));
    }
}
