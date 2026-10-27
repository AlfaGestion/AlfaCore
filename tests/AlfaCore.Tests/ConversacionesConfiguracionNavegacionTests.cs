using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Rediseño de Configuración (2026-10-05): el Asistente IA pasó a ser una sección propia, Automatización
/// se dividió en pantallas y cada pantalla guarda solo su sección. Los links viejos siguen funcionando.
/// </summary>
public sealed class ConversacionesConfiguracionNavegacionTests
{
    [Theory]
    [InlineData("integraciones-ia", "alfaknowledge", "asistente-ia", "conocimiento")]
    [InlineData("integraciones-ia", "informes-ia", "informes", "")]
    [InlineData("integraciones-ia", "asistente-ia", "asistente-ia", "general")]
    [InlineData("integraciones-ia", "", "asistente-ia", "general")]
    [InlineData("INTEGRACIONES-IA", "AlfaKnowledge", "asistente-ia", "conocimiento")]
    [InlineData("automatizacion", "horario-bienvenida", "automatizacion", "horario")]
    [InlineData("automatizacion", "reglas", "automatizacion", "reglas")]
    [InlineData("canales", "whatsapp-business", "canales", "whatsapp-business")]
    [InlineData("", "", "", "")]
    public void NormalizarSeccionLegacy_TraduceLinksViejos(string seccion, string subseccion, string esperadaSeccion, string esperadaSubseccion)
    {
        var (sec, sub) = ConversacionesConfiguracion.NormalizarSeccionLegacy(seccion, subseccion);

        Assert.Equal(esperadaSeccion, sec);
        Assert.Equal(esperadaSubseccion, sub);
    }

    [Theory]
    [InlineData("asistente-ia", "general", ConversacionAutomatizacionSeccion.Asistente)]
    [InlineData("asistente-ia", "herramientas", ConversacionAutomatizacionSeccion.Asistente)]
    [InlineData("informes", "", ConversacionAutomatizacionSeccion.Informe)]
    [InlineData("automatizacion", "horario", ConversacionAutomatizacionSeccion.Horario)]
    [InlineData("automatizacion", "", ConversacionAutomatizacionSeccion.Horario)]
    [InlineData("automatizacion", "bienvenida", ConversacionAutomatizacionSeccion.Bienvenida)]
    [InlineData("automatizacion", "auto-cierre", ConversacionAutomatizacionSeccion.AutoCierre)]
    [InlineData("automatizacion", "sla", ConversacionAutomatizacionSeccion.Sla)]
    [InlineData("automatizacion", "reglas", ConversacionAutomatizacionSeccion.Ninguna)]
    [InlineData("canales", "whatsapp-api", ConversacionAutomatizacionSeccion.Ninguna)]
    public void GetSeccionAutomatizacion_CadaPantallaGuardaSoloLoSuyo(string categoria, string subseccion, ConversacionAutomatizacionSeccion esperada)
        => Assert.Equal(esperada, ConversacionesConfiguracion.GetSeccionAutomatizacion(categoria, subseccion));

    [Fact]
    public void EstimarTokens_AproximaCuatroCaracteresPorToken()
    {
        Assert.Equal(0, ConversacionesConfiguracionAsistente.EstimarTokens(null));
        Assert.Equal(0, ConversacionesConfiguracionAsistente.EstimarTokens(string.Empty));
        Assert.Equal(1, ConversacionesConfiguracionAsistente.EstimarTokens("abc"));
        Assert.Equal(250, ConversacionesConfiguracionAsistente.EstimarTokens(new string('a', 1000)));
    }

    [Fact]
    public void InformacionExtensa_AvisaSoloPorEncimaDelUmbral()
    {
        var limite = ConversacionesConfiguracionAsistente.TokensInformacionAviso * 4;

        Assert.False(ConversacionesConfiguracionAsistente.InformacionExtensa(new string('a', limite)));
        Assert.True(ConversacionesConfiguracionAsistente.InformacionExtensa(new string('a', limite + 4)));
    }

    [Theory]
    [InlineData("7", 5, 1, 7)]
    [InlineData("", 5, 1, 5)]
    [InlineData("abc", 5, 1, 5)]
    [InlineData("0", 5, 1, 5)]
    [InlineData("0", 5, 0, 0)]
    [InlineData("-3", 2, 0, 2)]
    public void ParseEntero_ConservaElValorSiLaEntradaNoEsValida(string? valor, int actual, int minimo, int esperado)
        => Assert.Equal(esperado, ConversacionesConfiguracionAsistente.ParseEntero(valor, actual, minimo));

    [Fact]
    public void AlternarTecnico_AgregaYQuitaSinDuplicar()
    {
        var config = new ConversacionAutomatizacionesConfigDto { AsistenteUrgenciaTecnicos = ["T1", " t2 ", "T1"] };

        ConversacionesConfiguracionAsistente.AlternarTecnico(config, "T3", seleccionado: true);
        Assert.Equal(["T1", "t2", "T3"], config.AsistenteUrgenciaTecnicos);

        ConversacionesConfiguracionAsistente.AlternarTecnico(config, "T2", seleccionado: false);
        Assert.Equal(["T1", "T3"], config.AsistenteUrgenciaTecnicos);

        ConversacionesConfiguracionAsistente.AlternarTecnico(config, "T1", seleccionado: true);
        Assert.Equal(["T3", "T1"], config.AsistenteUrgenciaTecnicos);
        Assert.True(ConversacionesConfiguracionAsistente.TecnicoSeleccionado(config, " t1"));
        Assert.False(ConversacionesConfiguracionAsistente.TecnicoSeleccionado(config, "T2"));
    }

    [Fact]
    public void DescribirRegla_ResumeCanalCoincidenciaAccionesYCondiciones()
    {
        var regla = new ConversacionReglaDto
        {
            Canal = "WHATSAPP",
            TipoCoincidencia = "CONTIENE",
            Palabras = "turnos, horario",
            RespuestaTexto = "Atendemos de 9 a 18.",
            Prioridad = "ALTA",
            Horario = "FUERA",
            Detener = true
        };

        Assert.Equal(
            "WhatsApp · contiene: turnos, horario · responde · prioridad alta · fuera de horario · detiene",
            ConversacionesConfiguracionReglas.DescribirRegla(regla));
    }

    [Fact]
    public void AutomatizacionesEquals_DetectaCambiosDelAsistente()
    {
        var original = new ConversacionAutomatizacionesConfigDto { AsistenteInformacion = "Abrimos de 9 a 18." };
        var editado = new ConversacionAutomatizacionesConfigDto { AsistenteInformacion = "Abrimos de 9 a 19." };

        Assert.False(ConversacionesConfiguracion.AutomatizacionesEquals(original, editado));
        editado.AsistenteInformacion = original.AsistenteInformacion;
        Assert.True(ConversacionesConfiguracion.AutomatizacionesEquals(original, editado));
    }
}
