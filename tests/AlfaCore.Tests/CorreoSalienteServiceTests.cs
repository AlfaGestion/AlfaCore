using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Destinatarios de los mails de comprobantes: el campo email del cliente suele traer varios separados por ";".</summary>
public sealed class CorreoSalienteServiceTests
{
    [Fact]
    public void SepararMails_AceptaPuntoYComaComaYEspacios_SinRepetidos()
    {
        var (validos, invalidos) = CorreoSalienteService.SepararMails(" a@x.com; b@y.com.ar,c@z.com  A@X.com ;");

        Assert.Equal(["a@x.com", "b@y.com.ar", "c@z.com"], validos);
        Assert.Empty(invalidos);
    }

    [Fact]
    public void SepararMails_DevuelveLosInvalidosAparte()
    {
        var (validos, invalidos) = CorreoSalienteService.SepararMails("cliente@empresa.com;sin-arroba;otro@");

        Assert.Equal(["cliente@empresa.com"], validos);
        Assert.Equal(["sin-arroba", "otro@"], invalidos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SepararMails_VacioNoDevuelveNada(string? texto)
    {
        var (validos, invalidos) = CorreoSalienteService.SepararMails(texto);

        Assert.Empty(validos);
        Assert.Empty(invalidos);
    }
}
