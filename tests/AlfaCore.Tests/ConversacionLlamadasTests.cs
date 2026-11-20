using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConversacionLlamadasTests
{
    [Theory]
    [InlineData("5491153859509", "https://wa.me/5491153859509")]
    [InlineData("+54 9 11 5385-9509", "https://wa.me/5491153859509")]
    [InlineData("(011) 4567-8901", "https://wa.me/01145678901")]
    public void LinkWhatsApp_UsaSoloLosDigitos(string telefono, string esperado)
        => Assert.Equal(esperado, ConversacionLlamadas.LinkWhatsApp(telefono));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("1234567890123456")]
    [InlineData("sin numero")]
    public void LinkWhatsApp_SinNumeroValido_DevuelveNull(string? telefono)
        => Assert.Null(ConversacionLlamadas.LinkWhatsApp(telefono));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1", true)]
    [InlineData(" 0 ", false)]
    public void EstaActivo_SoloCeroLoApaga(string? valor, bool esperado)
        => Assert.Equal(esperado, ConversacionLlamadas.EstaActivo(valor));

    [Fact]
    public void ArmarNota_LlamadaHechaCompleta()
        => Assert.Equal(
            "📞 Llamada hecha · Atendió · 5 min · Pasa mañana a buscar el pedido",
            ConversacionLlamadas.ArmarNota(true, ConversacionLlamadas.ResultadoAtendio, 5, "  Pasa mañana\na buscar   el pedido "));

    [Fact]
    public void ArmarNota_RecibidaSinDuracionNiNota()
        => Assert.Equal(
            "📞 Llamada recibida · No atendió",
            ConversacionLlamadas.ArmarNota(false, ConversacionLlamadas.ResultadoNoAtendio, null, "   "));

    [Fact]
    public void ArmarNota_CeroMinutosNoSeMuestra()
        => Assert.Equal(
            "📞 Llamada hecha · Ocupado o cortó",
            ConversacionLlamadas.ArmarNota(true, ConversacionLlamadas.ResultadoOcupado, 0, null));

    [Fact]
    public void ArmarNota_SinResultado_PideElegirlo()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConversacionLlamadas.ArmarNota(true, "", 3, "x"));
        Assert.Equal("Elegí cómo fue la llamada.", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ConversacionLlamadas.MinutosMaximos + 1)]
    public void ArmarNota_DuracionFueraDeRango_Falla(int minutos)
        => Assert.Throws<InvalidOperationException>(
            () => ConversacionLlamadas.ArmarNota(true, ConversacionLlamadas.ResultadoAtendio, minutos, null));

    [Fact]
    public void ArmarNota_RecortaNotasLargas()
    {
        var texto = ConversacionLlamadas.ArmarNota(true, ConversacionLlamadas.ResultadoAtendio, null, new string('a', 900));
        Assert.EndsWith(new string('a', ConversacionLlamadas.NotaMaxima), texto);
        Assert.DoesNotContain(new string('a', ConversacionLlamadas.NotaMaxima + 1), texto);
    }
}
