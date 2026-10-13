using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Chat web embebible (canal WEBCHAT): validaciones de lo que llega de un visitante anónimo y de la
/// configuración que termina inyectada en el sitio del cliente.
/// </summary>
public sealed class ConversacionesWebChatTests
{
    [Fact]
    public void SiteKey_GeneradaTieneFormatoValidoYEsUnica()
    {
        var a = WebChatSiteKeys.Generate();
        var b = WebChatSiteKeys.Generate();

        Assert.True(WebChatSiteKeys.IsValidFormat(a));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AC-123")]
    [InlineData("AC-ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    [InlineData("' OR 1=1 --")]
    public void SiteKey_RechazaFormatosInvalidos(string? siteKey)
        => Assert.False(WebChatSiteKeys.IsValidFormat(siteKey));

    [Fact]
    public void Origen_SinDominiosConfiguradosAceptaCualquiera()
    {
        var config = new ConversacionWebChatConfigDto { DominiosPermitidos = "  " };

        Assert.True(config.IsOriginAllowed("https://cualquiera.com"));
        Assert.True(config.IsOriginAllowed(null));
    }

    [Theory]
    [InlineData("https://misitio.com", true)]
    [InlineData("https://www.misitio.com", true)]
    [InlineData("http://blog.misitio.com:8080", true)]
    [InlineData("https://otrositio.com.ar", true)]
    [InlineData("https://misitio.com.evil.com", false)]
    [InlineData("https://evilmisitio.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Origen_ConDominiosExigeCoincidenciaOSubdominio(string? origin, bool esperado)
    {
        var config = new ConversacionWebChatConfigDto { DominiosPermitidos = "https://misitio.com/, otrositio.com.ar" };

        Assert.Equal(esperado, config.IsOriginAllowed(origin));
    }

    [Theory]
    [InlineData("#12abEF", "#12abEF")]
    [InlineData("red", ConversacionWebChatConfigDto.DefaultColor)]
    [InlineData("#fff;}body{display:none", ConversacionWebChatConfigDto.DefaultColor)]
    [InlineData("", ConversacionWebChatConfigDto.DefaultColor)]
    public void Color_SoloAceptaHexDeSeisDigitos(string color, string esperado)
        => Assert.Equal(esperado, new ConversacionWebChatConfigDto { Color = color }.ColorSeguro);

    [Fact]
    public void Mensaje_NormalizaYRecortaCampos()
    {
        var request = new WebChatMensajeEntranteRequest
        {
            VisitorId = "  visitor-12345678  ",
            Texto = "  " + new string('x', WebChatMensajeEntranteRequest.MaxTexto + 50),
            Nombre = "   ",
            PaginaUrl = "javascript:alert(1)",
            Contexto = "actualizacion-v3-2",
            ClientMessageId = "<script>"
        };

        Assert.True(request.TryNormalize(out _));
        Assert.Equal("visitor-12345678", request.VisitorId);
        Assert.Equal(WebChatMensajeEntranteRequest.MaxTexto, request.Texto.Length);
        Assert.Null(request.Nombre);
        Assert.Null(request.PaginaUrl);
        Assert.Equal("actualizacion-v3-2", request.Contexto);
        Assert.Matches("^[a-f0-9]{32}$", request.ClientMessageId);
    }

    [Fact]
    public void Mensaje_ConservaUrlHttpsYClientId()
    {
        var request = new WebChatMensajeEntranteRequest
        {
            VisitorId = "abcdefgh",
            Texto = "Hola",
            PaginaUrl = "https://misitio.com/actualizacion-v3-2/",
            ClientMessageId = "msg_01"
        };

        Assert.True(request.TryNormalize(out _));
        Assert.Equal("https://misitio.com/actualizacion-v3-2/", request.PaginaUrl);
        Assert.Equal("msg_01", request.ClientMessageId);
    }

    [Theory]
    [InlineData("corto", "Hola")]
    [InlineData("abcdefgh", "   ")]
    [InlineData("con espacios 123", "Hola")]
    public void Mensaje_RechazaVisitanteInvalidoOTextoVacio(string visitorId, string texto)
    {
        var request = new WebChatMensajeEntranteRequest { VisitorId = visitorId, Texto = texto };

        Assert.False(request.TryNormalize(out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
