using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ArcaCodigosDocumentoTests
{
    [Fact]
    public void CuitConGuiones_SeEnviaSoloConDigitos()
        => Assert.Equal((80, "30688774930"), ArcaCodigosAfip.ResolverDocumento("C.U.I.T.", "30-68877493-0"));

    [Theory]
    [InlineData("D.N.I.", "0")]
    [InlineData("D.N.I.", "              0")]
    [InlineData("D.N.I.", "")]
    [InlineData("C.U.I.T.", "00-00000000-0")]
    public void DocumentoSinNumero_SeInformaComoConsumidorFinal(string tipo, string numero)
        => Assert.Equal((99, "0"), ArcaCodigosAfip.ResolverDocumento(tipo, numero));

    [Fact]
    public void DniValido_SeConserva()
        => Assert.Equal((96, "27776846"), ArcaCodigosAfip.ResolverDocumento("D.N.I.", "27.776.846"));
}
