using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bot (2026-10-15): "Prueba Chat Web" abrió el ticket #79 porque el modelo contestó "parece que
/// estás probando" pero marcó DERIVA. El ticket automático queda sólo para consultas reales.
/// </summary>
public sealed class ConversacionesBotTicketConsultaTests
{
    [Theory]
    [InlineData("Prueba Chat Web")]
    [InlineData("prueba")]
    [InlineData("Test")]
    [InlineData("Hola, probando")]
    [InlineData("Esto es una prueba")]
    [InlineData("Hola")]
    [InlineData("Muchas gracias")]
    [InlineData("a")]
    [InlineData("?")]
    [InlineData("...")]
    [InlineData("")]
    public void NoEsConsulta_NoGeneraTicket(string texto)
        => Assert.False(ConversacionesService.EsConsultaParaTicket(texto));

    [Theory]
    [InlineData("No me imprime la factura")]
    [InlineData("¿Cómo cargo un proveedor nuevo?")]
    [InlineData("Hago una prueba de factura y me da error")]
    [InlineData("No anda")]
    public void ConsultaReal_GeneraTicket(string texto)
        => Assert.True(ConversacionesService.EsConsultaParaTicket(texto));

    [Theory]
    [InlineData("a")]
    [InlineData("A")]
    [InlineData("?")]
    [InlineData("¿?")]
    [InlineData("...")]
    [InlineData(" k ")]
    public void MensajeSinContenido_SeReconoce(string texto)
        => Assert.True(ConversacionesService.EsMensajeSinContenido(texto));

    [Theory]
    [InlineData("No")]
    [InlineData("Sí")]
    [InlineData("ok")]
    [InlineData("45")]
    [InlineData("1")]
    [InlineData("👍")]
    [InlineData("Hola")]
    [InlineData("")]
    public void RespuestasCortasValidas_NoSonMensajeSinContenido(string texto)
        => Assert.False(ConversacionesService.EsMensajeSinContenido(texto));

    [Fact]
    public void ElBotPideQueAclareUnMensajeSinContenidoYNoInsiste()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("if (EsMensajeSinContenido(texto) && ultimaRespuestaFueAclaracion)", source, StringComparison.Ordinal);
        Assert.Contains("No llegué a entender tu mensaje. ¿Me contás en qué te puedo ayudar?", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LaPreguntaDeIdentidadNoCuentaComoSegundaAclaracion()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("if (ultimaRespuestaFueAclaracion && !identidadPreguntadaAhora && string.Equals(result?.Tipo, \"ACLARA\"", source, StringComparison.Ordinal);
        Assert.Contains("if (tipo == \"ACLARA\" && !identidadPreguntadaAhora)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SiPideHablarConUnaPersonaSeLeAvisaUnaVezYQuedaNota()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("ExisteNotaInternaHoyAsync(idConversacion, NotaPidioPersona, token)", source, StringComparison.Ordinal);
        Assert.Contains("Texto = RespuestaPidioPersona", source, StringComparison.Ordinal);
        Assert.Contains("te paso con alguien del equipo", ConversacionesService.RespuestaPidioPersona, StringComparison.Ordinal);
    }

    [Fact]
    public void ElBotNoDerivaNiAbreTicketAnteUnMensajeDePrueba()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        Assert.Contains("if (EsMensajeDePrueba(texto) && !string.Equals(result?.Tipo, \"RESUELVE\"", source, StringComparison.Ordinal);
        Assert.Contains("if (tipo == \"DERIVA\" && config.BotGeneraTicket && !omitirTicket && !correccionSinTicket && EsConsultaParaTicket(texto))", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pudieron ver algo?")]
    [InlineData("si,ayudame con lo que pedimos ayer")]
    [InlineData("¿Novedades?")]
    [InlineData("Hola, alguna novedad?")]
    [InlineData("sigo esperando")]
    [InlineData("hola,pudiste ver algo?")]
    public void PedidoDeNovedades_SeReconoce(string texto)
        => Assert.True(ConversacionesService.PideNovedades(texto));

    [Theory]
    [InlineData("No me imprime la factura")]
    [InlineData("buen día")]
    [InlineData("Quiero dar de alta un proveedor nuevo y no encuentro la opción en el menú de compras")]
    public void ConsultaNueva_NoEsPedidoDeNovedades(string texto)
        => Assert.False(ConversacionesService.PideNovedades(texto));

    [Fact]
    public void PedidoDeNovedades_RespondeSinNombrarANadieYSinTicket()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        Assert.DoesNotContain("{", ConversacionesService.RespuestaSeguimiento, StringComparison.Ordinal);
        Assert.Contains("tema pendiente", ConversacionesService.RespuestaSeguimiento, StringComparison.Ordinal);
        Assert.Matches(@"await ResponderSeguimientoAsync\(idConversacion, pendiente, token\)\.ConfigureAwait\(false\);\s*return;", source);
        Assert.Contains("!omitirTicket && !correccionSinTicket && EsConsultaParaTicket(texto)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ya esta arreglado")]
    [InlineData("Ya está arreglado, gracias!")]
    [InlineData("ya anda")]
    [InlineData("Listo, ya me funciona")]
    [InlineData("lo pude solucionar, ya lo solucioné")]
    [InlineData("Se arregló solo, gracias")]
    [InlineData("ya no hace falta")]
    public void AvisoDeTemaResuelto_SeReconoce(string texto)
        => Assert.True(ConversacionesService.AvisaTemaResuelto(texto));

    [Theory]
    [InlineData("¿ya está arreglado?")]
    [InlineData("todavía no anda")]
    [InlineData("ya anda pero me sale otro error")]
    [InlineData("sigue sin funcionar")]
    [InlineData("pudieron ver algo?")]
    [InlineData("No me imprime la factura")]
    public void PreguntaONegacion_NoEsAvisoDeResuelto(string texto)
        => Assert.False(ConversacionesService.AvisaTemaResuelto(texto));

    [Fact]
    public void AvisoDeResuelto_SeAtiendeAntesQueElPedidoDeNovedades()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var resuelto = source.IndexOf("if (AvisaTemaResuelto(texto))", StringComparison.Ordinal);
        var novedades = source.IndexOf("var omitirTicket = false;", StringComparison.Ordinal);
        Assert.True(resuelto > 0 && resuelto < novedades);
        Assert.DoesNotContain("?", ConversacionesService.RespuestaTemaResuelto, StringComparison.Ordinal);
    }

    [Fact]
    public void AvisoDeResuelto_QuedaAsentadoEnElTicketConElMensajeDelCliente()
    {
        var nota = ConversacionesService.ConstruirNotaAvisoResuelto("ya esta arreglado", "WHATSAPP", "Legión S. A.", 10482, new DateTime(2026, 10, 19, 10, 50, 0));

        Assert.Contains("Legión S. A.", nota, StringComparison.Ordinal);
        Assert.Contains("por WhatsApp", nota, StringComparison.Ordinal);
        Assert.Contains("\"ya esta arreglado\"", nota, StringComparison.Ordinal);
        Assert.Contains("19/10/2026 10:50", nota, StringComparison.Ordinal);
        Assert.Contains("conversación #10482", nota, StringComparison.Ordinal);

        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("await AsentarAvisoResueltoEnTicketAsync(idTicket, idConversacion, pendiente, textoCliente, ct)", source, StringComparison.Ordinal);
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
