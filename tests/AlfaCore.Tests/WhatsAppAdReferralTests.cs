using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppAdReferralTests
{
    [Fact]
    public void LeeElAnuncioDelMensajeGuardado()
    {
        const string payload = """
            {"from":"5491100000000","id":"wamid.X","type":"text","text":{"body":"¡Hola! Quiero más información"},
             "referral":{"source_url":"https://fb.me/abc","source_id":"123","source_type":"ad","headline":"Calendario de Adviento","body":"Cajas para regalar","media_type":"image","ctwa_clid":"zzz"}}
            """;

        Assert.True(WhatsAppAdReferral.TryParse(payload, out var referral));
        Assert.Equal("Calendario de Adviento", referral.Titulo);
        Assert.Equal("Cajas para regalar", referral.Texto);
        Assert.Equal("https://fb.me/abc", referral.Url);
        Assert.Equal("Anuncio", referral.OrigenNombre);
    }

    [Fact]
    public void PublicacionSeNombraComoPublicacion()
    {
        const string payload = """{"referral":{"source_type":"post","source_url":"https://www.instagram.com/p/x/"}}""";

        Assert.True(WhatsAppAdReferral.TryParse(payload, out var referral));
        Assert.Equal("Publicación", referral.OrigenNombre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json \"referral\"")]
    [InlineData("""{"type":"text","text":{"body":"hola"}}""")]
    [InlineData("""{"referral":{"source_url":"javascript:alert(1)"}}""")]
    public void SinAnuncioUtilNoMuestraNada(string? payload)
        => Assert.False(WhatsAppAdReferral.TryParse(payload, out _));
}
