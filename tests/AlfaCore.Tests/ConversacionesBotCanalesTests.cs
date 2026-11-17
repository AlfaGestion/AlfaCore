using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bot por canal, ticket al no encontrar respuesta y datos del visitante del chat web (2026-10-09).
/// </summary>
public sealed class ConversacionesBotCanalesTests
{
    [Fact]
    public void Canales_SinConfigurar_RespondeEnTodos()
    {
        var canales = ConversacionesConfigService.ParseBotCanales(null);

        Assert.Equal(ConversacionAutomatizacionesConfigDto.BotCanalesDisponibles.Count, canales.Count);
        var config = new ConversacionAutomatizacionesConfigDto { BotCanales = canales };
        Assert.True(config.BotRespondeEnCanal("WHATSAPP"));
        Assert.True(config.BotRespondeEnCanal("webchat"));
    }

    [Fact]
    public void Canales_Configurados_SoloRespondeEnLosElegidos()
    {
        var config = new ConversacionAutomatizacionesConfigDto
        {
            BotCanales = ConversacionesConfigService.ParseBotCanales("WEBCHAT, INSTAGRAM")
        };

        Assert.True(config.BotRespondeEnCanal("WEBCHAT"));
        Assert.True(config.BotRespondeEnCanal("INSTAGRAM"));
        Assert.False(config.BotRespondeEnCanal("WHATSAPP"));
        Assert.False(config.BotRespondeEnCanal("FACEBOOK"));
        Assert.False(config.BotRespondeEnCanal(null));
    }

    [Fact]
    public void Canales_NingunoMarcado_SeGuardaComoNinguno_YNoRespondeEnNinguno()
    {
        var guardado = ConversacionesConfigService.FormatBotCanales([]);

        Assert.Equal(ConversacionesConfigService.BotCanalesNinguno, guardado);
        Assert.Empty(ConversacionesConfigService.ParseBotCanales(guardado));
    }

    [Fact]
    public void Canales_IgnoraCodigosDesconocidosYDuplicados()
    {
        Assert.Equal(["WEBCHAT"], ConversacionesConfigService.ParseBotCanales("webchat,WEBCHAT,TELEGRAM"));
        Assert.Equal("WHATSAPP,WEBCHAT", ConversacionesConfigService.FormatBotCanales(["whatsapp", "WEBCHAT", "otro"]));
    }

    // Chat web (2026-11-17): los datos los interpreta la IA y llegan por guardar_contacto_web; el
    // servidor solo valida el teléfono. Antes "2944000002, no tengo empresa" quedaba como razón social "no tengo".
    [Theory]
    [InlineData("{\"telefono\":\"+54 9 294 400-0002\",\"nombre\":\"Juana Pérez\"}", "5492944000002", "Juana Pérez")]
    [InlineData("{\"telefono\":\"2944000002\"}", "2944000002", "")]
    [InlineData("{\"telefono\":\"11 5555-1234\",\"nombre\":\"  Ferretería Gómez SRL \"}", "1155551234", "Ferretería Gómez SRL")]
    public void ContactoWeb_LeeTelefonoYNombreQueInterpretoLaIa(string argumentos, string telefono, string nombre)
    {
        Assert.True(ConversacionesService.TryLeerContactoWeb(argumentos, out var tel, out var nom));
        Assert.Equal(telefono, tel);
        Assert.Equal(nombre, nom);
    }

    [Theory]
    [InlineData("{\"telefono\":\"123\"}")]
    [InlineData("{\"nombre\":\"Juana\"}")]
    [InlineData("no es json")]
    [InlineData("")]
    public void ContactoWeb_SinTelefonoValido_NoSeGuarda(string argumentos)
        => Assert.False(ConversacionesService.TryLeerContactoWeb(argumentos, out _, out _));

    [Fact]
    public void ContactoWeb_LaHerramientaNoPideRazonSocialObligatoria()
    {
        var herramienta = ConversacionesService.HerramientaGuardarContactoWeb;
        Assert.Equal("guardar_contacto_web", herramienta.Nombre);
        Assert.Contains("\"required\":[\"telefono\"]", herramienta.ParametrosJsonSchema);
    }
}
