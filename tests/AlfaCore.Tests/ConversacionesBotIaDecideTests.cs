using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bot (2026-11-25): lo que quiere decir el cliente lo interpreta la IA leyendo la conversación. Antes listas
/// de palabras pisaban su respuesta: "2,3y4" (cajas 2, 3 y 4) quedaba reducido a la "y" y se contestaba
/// "¡Hola! ¿En qué te puedo ayudar?" (Supermercado Manantial, ALFANET). El código sólo respeta lo que decide
/// la IA y conserva los controles de seguridad (borrador antes de grabar, identidad antes de datos privados).
/// </summary>
public sealed class ConversacionesBotIaDecideTests
{
    private static string Prompt()
        => ConversacionAsistenteService.BuildSystemPrompt("", "", "", false, false, null, null, null);

    private static string Fuente()
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));

    [Fact]
    public void ElPrompt_PideInterpretarConLaConversacion_SinFrasesDeEjemplo()
    {
        var prompt = Prompt();
        Assert.Contains("Leé el último mensaje junto con toda la conversación", prompt, StringComparison.Ordinal);
        Assert.Contains("uno por renglón", prompt, StringComparison.Ordinal);
        Assert.Contains("Un dato suelto (números, un nombre, un sí o un no) contesta lo que se venía hablando", prompt, StringComparison.Ordinal);
        Assert.Contains("\"tema_resuelto\": true/false", prompt, StringComparison.Ordinal);
        // Las frases de ejemplo terminaban funcionando como disparadores.
        Assert.DoesNotContain("ya anda", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("¿pudieron ver algo?", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"tipo":"RESUELVE","puede_responder":true,"tema_resuelto":true,"respuesta":"¡Qué bueno!"}""", true)]
    [InlineData("""{"tipo":"RESUELVE","puede_responder":true,"tema_resuelto":"true","respuesta":"Listo"}""", true)]
    [InlineData("""{"tipo":"RESUELVE","puede_responder":true,"respuesta":"Hola"}""", false)]
    public void TemaResuelto_LoDecideLaIa(string json, bool esperado)
        => Assert.Equal(esperado, ConversacionAsistenteService.ParseRespuesta(json)!.TemaResuelto);

    [Theory]
    [InlineData("""{"confirma":true}""", true, true)]
    [InlineData("""{"confirma":false}""", true, false)]
    [InlineData("""{"confirma":"true"}""", true, true)]
    [InlineData("""{}""", false, false)]
    [InlineData("no es json", false, false)]
    public void Confirmaciones_LeenLoQueDecidioLaIa(string argumentos, bool valido, bool confirma)
    {
        Assert.Equal(valido, ConversacionesService.TryLeerConfirma(argumentos, out var leido));
        Assert.Equal(confirma, leido);
    }

    [Fact]
    public void NumeroDePedido_LoPasaLaIaALaHerramienta()
    {
        Assert.True(ConversacionesService.TryLeerTexto("""{"numero_pedido":"0003X"}""", "numero_pedido", out var numero));
        Assert.Equal("0003X", numero);
        Assert.False(ConversacionesService.TryLeerTexto("""{}""", "numero_pedido", out _));
        Assert.False(ConversacionesService.TryLeerTexto("""{"numero_pedido":"  "}""", "numero_pedido", out _));
    }

    [Fact]
    public void HerramientasDeConfirmacion_SoloConAlgoPendiente()
    {
        var source = Fuente();
        Assert.Contains("if (identidadPendiente && identidadYaPreguntada)", source, StringComparison.Ordinal);
        Assert.Contains("if (pedidoPendiente)", source, StringComparison.Ordinal);
        Assert.Contains("if (altaPendiente)", source, StringComparison.Ordinal);
        // El servidor graba sólo si hay borrador: la IA decide, el control de seguridad queda.
        Assert.Contains("nombreHerramienta == ToolConfirmarPedido && pedidoPendiente && botPedidosService is not null", source, StringComparison.Ordinal);
        Assert.Contains("nombreHerramienta == ToolConfirmarAltaCliente && altaPendiente && botClientesService is not null", source, StringComparison.Ordinal);
        Assert.Contains("if (identidadNegadaAhora && EsHerramientaConDatosPrivados(nombreHerramienta))", source, StringComparison.Ordinal);
        Assert.Equal("confirma", System.Text.Json.JsonDocument.Parse(ConversacionesService.HerramientaConfirmarPedido.ParametrosJsonSchema)
            .RootElement.GetProperty("required")[0].GetString());
    }

    [Fact]
    public void NingunaReglaDePalabrasPisaLaRespuestaDeLaIa()
    {
        var source = Fuente();
        Assert.DoesNotContain("RespuestaSaludo(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EsMensajeDePrueba(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EsRespuestaSuelta(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AvisaTemaResuelto(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PideNovedades(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EsRespuestaIdentidad", source, StringComparison.Ordinal);
        Assert.DoesNotContain("eligioPedido", source, StringComparison.Ordinal);
        // Ticket: lo decide abrir_ticket de la IA.
        Assert.Contains("!omitirTicket && !correccionSinTicket && abrirTicketSegunIa)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PedidoOTicket_SoloLosPedidosEnCursoCompitenConElTicket_YNoSeRepreguntan()
    {
        const string terminados = "- Pedido 0001X del 01/10/2026: Finalizado — $ 1.000,00\n- Pedido 0002X del 02/10/2026: Anulado — $ 500,00";
        const string enCurso = "- Pedido 0001X del 01/10/2026: Finalizado — $ 1.000,00\n- Pedido 0003X del 27/10/2026: Aprobado (en preparación) — $ 7.000,00";

        Assert.Null(ConversacionAsistenteHerramientasService.PrimerPedidoEnCurso(terminados));
        Assert.Equal("0003X", ConversacionAsistenteHerramientasService.PrimerPedidoEnCurso(enCurso));
        Assert.Equal("¿Me preguntás por tu pedido 0003X o por tu consulta en curso (ticket #1)?",
            ConversacionesService.PreguntaPedidoOTicket("0003X", 1));
        Assert.Contains("if (!yaPreguntoPedidoOTicket && !pidioPedidoPuntual)", Fuente(), StringComparison.Ordinal);
    }

    [Fact]
    public void Seguimiento_RespondeSinNombrarANadie_YTemaResueltoVaAntes()
    {
        var source = Fuente();
        Assert.DoesNotContain("{", ConversacionesService.RespuestaSeguimiento, StringComparison.Ordinal);
        Assert.Matches(@"await ResponderSeguimientoAsync\(idConversacion, pendiente, token\)\.ConfigureAwait\(false\);\s*return;", source);
        var resuelto = source.IndexOf("if (result?.TemaResuelto == true)", StringComparison.Ordinal);
        var novedades = source.IndexOf("var omitirTicket = false;", StringComparison.Ordinal);
        Assert.True(resuelto > 0 && resuelto < novedades);
        Assert.DoesNotContain("?", ConversacionesService.RespuestaTemaResuelto, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        return current!.FullName;
    }
}
