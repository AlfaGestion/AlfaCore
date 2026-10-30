using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CobranzaPagadoresTests
{
    private static CobranzaDocumento Doc(int id,decimal saldo)=>new(){Id=id,Saldo=saldo};
    [Fact]
    public void EncuentraDosFacturasAunqueElTotalDelClienteSeaMayor()
    {
        var docs=new[]{Doc(1,500),Doc(2,360),Doc(3,1000),Doc(4,900)};
        var (ids,_) = CobranzaPagadoresCoincidencias.Buscar(docs,860,0);
        Assert.Equal(new[]{1,2},ids.Order());
    }
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CombinaHastaCincoFacturas(int cantidad)
    {
        var docs=Enumerable.Range(1,cantidad).Select(i=>Doc(i,10)).ToList();
        Assert.Equal(cantidad,CobranzaPagadoresCoincidencias.Buscar(docs,10*cantidad,0).Ids.Count);
    }
    [Fact]
    public void PriorizaFacturaIndividualCercanaYSenalaFaltaDeCombinacionSinInferirPagador()
    {
        var docs=new[]{Doc(1,100.39m),Doc(2,50),Doc(3,50)};
        Assert.Equal(new[]{1},CobranzaPagadoresCoincidencias.Buscar(docs,100,.5m).Ids);
        Assert.Equal(2,CobranzaPagadoresCoincidencias.Buscar(docs,100,0).Ids.Count);
        Assert.Empty(CobranzaPagadoresCoincidencias.Buscar(docs,250,0).Ids);
    }
    [Fact]
    public void InformaLimiteSinOcultarFacturasYCancelaLaBusqueda()
    {
        var docs=Enumerable.Range(1,1000).Select(i=>Doc(i,2)).ToList();
        var resultado=CobranzaPagadoresCoincidencias.Buscar(docs,9,0);
        Assert.Empty(resultado.Ids);Assert.False(resultado.Completa);Assert.Equal(1000,docs.Count);
        using var cts=new CancellationTokenSource();cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>CobranzaPagadoresCoincidencias.Buscar(docs,9,0,cts.Token));
    }
}
