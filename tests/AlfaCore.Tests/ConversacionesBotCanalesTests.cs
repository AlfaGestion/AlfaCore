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

    [Theory]
    [InlineData("Mi WhatsApp es 11 5555-1234, razón social Ferretería Gómez SRL", "1155551234", "Ferretería Gómez SRL")]
    [InlineData("+54 9 11 2345 6789 - Distribuidora Norte", "5491123456789", "Distribuidora Norte")]
    [InlineData("1123456789", "1123456789", "")]
    [InlineData("La empresa es Panadería La Plata y mi celular 2214567890", "2214567890", "Panadería La Plata")]
    public void DatosVisitante_ExtraeTelefonoYRazonSocial(string texto, string telefono, string razon)
    {
        Assert.True(ConversacionesService.TryParseDatosVisitanteWeb(texto, out var tel, out var razonSocial));
        Assert.Equal(telefono, tel);
        Assert.Equal(razon, razonSocial);
    }

    [Theory]
    [InlineData("Quiero saber el precio del producto 123")]
    [InlineData("Hola, ¿tienen envíos?")]
    [InlineData("")]
    public void DatosVisitante_SinTelefono_NoLosToma(string texto)
        => Assert.False(ConversacionesService.TryParseDatosVisitanteWeb(texto, out _, out _));
}
