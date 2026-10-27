using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ArcaCodigosAfipTests
{
    [Theory]
    [InlineData("IVA RESPONSABLE INSCRIPTO", "1", 1, "A")]
    [InlineData("RESPONSABLE MONOTRIBUTO", "5", 6, "A")]
    [InlineData("MONOTRIBUTISTA SOCIAL", "", 13, "A")]
    [InlineData("MONOTRIBUTO TRABAJADOR INDEPENDIENTE PROMOVIDO", "", 16, "A")]
    [InlineData("IVA EXENTO", "4", 4, "B")]
    [InlineData("CONSUMIDOR FINAL", "3", 5, "B")]
    public void CondicionIvaYLetraRespetanLasCombinacionesValidasDeArca(
        string descripcion,
        string codigoLocal,
        int codigoArca,
        string letra)
    {
        var resuelta = ArcaCodigosAfip.ResolverCondicionIvaReceptor(descripcion, codigoLocal);

        Assert.Equal(codigoArca, resuelta);
        Assert.True(ArcaCodigosAfip.EsCondicionValidaParaLetra(resuelta, letra));
    }

    [Fact]
    public void MonotributoNoEsValidoParaFacturaB()
    {
        var codigo = ArcaCodigosAfip.ResolverCondicionIvaReceptor("RESPONSABLE MONOTRIBUTO", "5");

        Assert.False(ArcaCodigosAfip.EsCondicionValidaParaLetra(codigo, "B"));
        Assert.True(ArcaCodigosAfip.EsCondicionValidaParaLetra(codigo, "A"));
    }
}
