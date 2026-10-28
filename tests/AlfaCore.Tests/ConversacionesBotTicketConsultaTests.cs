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

    [Theory]
    [InlineData("necesito hablar con una persona", true)]
    [InlineData("quiero hablar con personas", true)]
    [InlineData("hacen capacitaciones para el personal?", false)]
    [InlineData("tengo el sistema caído", true)]
    public void PalabrasDeEscalado_ComparanPalabrasCompletasSinTildes(string texto, bool esperado)
        => Assert.Equal(esperado, ConversacionesService.ContienePalabraEscalado(texto, "humano, persona, reclamo, sistema caido"));

    [Fact]
    public void MailQueNoFiguraEnNingunLado_SeDetecta()
    {
        Assert.Equal(["soporte@alfagestion.com"],
            ConversacionAsistenteService.MailsSinRespaldo("El mail de soporte es soporte@alfagestion.com.", "Mail: (sin dato cargado)"));
        Assert.Empty(ConversacionAsistenteService.MailsSinRespaldo("Escribinos a ventas@x.com", "CANALES: ventas@x.com"));
    }

    [Theory]
    [InlineData("si", true)]
    [InlineData("Sí", true)]
    [InlineData("no", true)]
    [InlineData("jaja", true)]
    [InlineData("si, quiero 3 aa", false)]
    [InlineData("no me anda el sistema", false)]
    public void RespuestaSuelta_NoEsConsultaParaTicket(string texto, bool suelta)
    {
        Assert.Equal(suelta, ConversacionesService.EsRespuestaSuelta(texto));
        Assert.Equal(!suelta, ConversacionesService.EsConsultaParaTicket(texto));
    }

    [Fact]
    public void InformacionConTextosDePlantillaSinCompletar_NoLlegaComoDato()
    {
        var info = "- Soporte: [días y horario, ej: lunes a viernes de 9 a 18 hs].\n- WhatsApp: [número].\nManual: [ver acá](https://x.com/m) y código [A1].";
        var limpio = ConversacionAsistenteService.QuitarTextosSinCompletar(info);
        Assert.DoesNotContain("lunes a viernes", limpio);
        Assert.Contains("- Soporte: (sin dato cargado).", limpio);
        Assert.Contains("- WhatsApp: (sin dato cargado).", limpio);
        Assert.Contains("[ver acá](https://x.com/m)", limpio);
        Assert.Contains("[A1]", limpio);
    }

    [Theory]
    [InlineData("no", true)]
    [InlineData("No.", true)]
    [InlineData("no, te equivocaste de empresa", true)]
    [InlineData("no soy yo", true)]
    [InlineData("nada que ver", true)]
    [InlineData("no, mejor 4 aaa nomas", false)]
    [InlineData("no, cancelalo", false)]
    [InlineData("no, quiero 3 nada mas", false)]
    [InlineData("si", false)]
    public void RespuestaIdentidadNo_NoConfundeCambiosDelPedido(string texto, bool esperado)
        => Assert.Equal(esperado, ConversacionesService.EsRespuestaIdentidadNo(texto));

    [Theory]
    [InlineData("Te recomiendo que contactes a un asesor para que te ayude.", true)]
    [InlineData("Te recomiendo que consultes directamente con un asesor.", true)]
    [InlineData("Comunicate con nuestro equipo de ventas.", true)]
    [InlineData("Le paso tu consulta a un asesor y te responde a la brevedad.", false)]
    [InlineData("Lo consulto con el equipo y te responden por acá.", false)]
    [InlineData("Te recomiendo consultar con un asesor si los precios incluyen IVA.", true)]
    public void Contencion_QuePideContactarAOtro_SeDetecta(string respuesta, bool esperado)
        => Assert.Equal(esperado, ConversacionesService.PideContactarAOtro(respuesta));

    [Theory]
    [InlineData("No vendo resmas de papel.", true)]
    [InlineData("No vendemos ese producto.", true)]
    [InlineData("No lo encontré en la lista de artículos.", false)]
    public void Respuesta_QueAfirmaQueNoSeVende_SeDetecta(string respuesta, bool esperado)
        => Assert.Equal(esperado, ConversacionesService.AfirmaQueNoSeVende(respuesta));

    [Theory]
    [InlineData("Le paso tu consulta a un asesor.", true)]
    [InlineData("Un compañero te responde a la brevedad.", true)]
    [InlineData("Parece que tu mensaje se cortó. ¿Me contás qué necesitás?", false)]
    public void Derivacion_MencionaAlEquipo(string respuesta, bool esperado)
        => Assert.Equal(esperado, ConversacionesService.MencionaAlEquipo(respuesta));

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
