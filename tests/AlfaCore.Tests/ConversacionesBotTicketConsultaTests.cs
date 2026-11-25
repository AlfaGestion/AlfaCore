using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bot (2026-10-15): "Prueba Chat Web" abrió el ticket #79 porque el modelo contestó "parece que
/// estás probando" pero marcó DERIVA. El ticket automático queda sólo para consultas reales.
/// </summary>
public sealed class ConversacionesBotTicketConsultaTests
{
    [Fact]
    public void LaPreguntaDeIdentidadNoCuentaComoSegundaAclaracion()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("if (ultimaRespuestaFueAclaracion && !identidadPreguntadaAhora && !pedidoPreparadoAhora && string.Equals(result?.Tipo, \"ACLARA\"", source, StringComparison.Ordinal);
        Assert.Contains("if (tipo == \"ACLARA\" && !identidadPreguntadaAhora && !pedidoPreparadoAhora)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumenDePedidoYReintentoPorInterbloqueo_EnElFlujoDelBot()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("!identidadPreguntadaAhora && !pedidoPreparadoAhora && string.Equals(result?.Tipo, \"ACLARA\"", source, StringComparison.Ordinal);
        Assert.Contains("catch (SqlException ex) when (ex.Number == 1205 && intento < 3)", source, StringComparison.Ordinal);
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
    [InlineData("Te recomiendo que contactes a un asesor para que te ayude.", true)]
    [InlineData("Te recomiendo que consultes directamente con un asesor.", true)]
    [InlineData("Comunicate con nuestro equipo de ventas.", true)]
    [InlineData("Le paso tu consulta a un asesor y te responde a la brevedad.", false)]
    [InlineData("Lo consulto con el equipo y te responden por acá.", false)]
    [InlineData("Te recomiendo consultar con un asesor si los precios incluyen IVA.", true)]
    [InlineData("Te recomiendo que hables con un compañero del equipo.", true)]
    [InlineData("Te recomiendo que contactes a un operador del equipo.", true)]
    [InlineData("Te recomiendo que le pases tu consulta a un asesor.", true)]
    [InlineData("Te paso tu consulta a un asesor y te responde a la brevedad.", false)]
    public void Contencion_QuePideContactarAOtro_SeDetecta(string respuesta, bool esperado)
        => Assert.Equal(esperado, ConversacionesService.PideContactarAOtro(respuesta));

    [Theory]
    [InlineData("No vendo resmas de papel.", true)]
    [InlineData("No vendemos ese producto.", true)]
    [InlineData("No lo encontré en la lista de artículos.", false)]
    public void Respuesta_QueAfirmaQueNoSeVende_SeDetecta(string respuesta, bool esperado)
        => Assert.Equal(esperado, ConversacionesService.AfirmaQueNoSeVende(respuesta));

    [Fact]
    public void SiPideHablarConUnaPersonaSeLeAvisaUnaVezYQuedaNota()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("ExisteNotaInternaHoyAsync(idConversacion, NotaPidioPersona, token)", source, StringComparison.Ordinal);
        Assert.Contains("Texto = RespuestaPidioPersona", source, StringComparison.Ordinal);
        Assert.Contains("te paso con alguien del equipo", ConversacionesService.RespuestaPidioPersona, StringComparison.Ordinal);
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
