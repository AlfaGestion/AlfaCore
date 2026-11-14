using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Controles de «Incluir en libro de IVA» desde un asiento (por ejemplo la devolución de una retención de IIBB).</summary>
public sealed class AsientosLibroIvaTests
{
    private const decimal ImporteAsiento = 14_880.27m;

    private static AsientoLibroIva Libro(Action<AsientoLibroIva>? ajustar = null)
    {
        var l = new AsientoLibroIva { Nombre = "Banco de la Nacion Argentina", Cuit = "30-50001091-2", RetencionIibb = ImporteAsiento };
        ajustar?.Invoke(l);
        return l;
    }

    [Fact]
    public void DevolucionDeRetencionSinNetoNiIvaPasa()
        => AsientosService.ValidarLibroIva(Libro(), ImporteAsiento);

    [Fact]
    public void TotalMenorAlAsientoPasa()
        => AsientosService.ValidarLibroIva(Libro(l => l.RetencionIibb = 100m), ImporteAsiento);

    [Fact]
    public void ElLibroNoPuedeSuperarElImporteDelAsiento()
    {
        var ex = Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => l.RetencionIibb = ImporteAsiento + 0.01m), ImporteAsiento));
        Assert.Contains("supera", ex.Message);
    }

    [Fact]
    public void LaSumaDeTodosLosConceptosCuentaParaElTope()
        => Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => { l.RetencionIibb = 10_000m; l.NoGravado = 5_000m; }), ImporteAsiento));

    [Fact]
    public void SinImportesNoPasa()
        => Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => l.RetencionIibb = 0), ImporteAsiento));

    [Theory]
    [InlineData("", "30-50001091-2")]
    [InlineData("Banco", "30-50001091-3")]
    [InlineData("Banco", "")]
    public void EmisorYCuitSonObligatorios(string nombre, string cuit)
        => Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => { l.Nombre = nombre; l.Cuit = cuit; }), ImporteAsiento));

    [Fact]
    public void ImportesNegativosOConTresDecimalesNoPasan()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => l.RetencionIibb = -1m), ImporteAsiento));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => l.RetencionIibb = 10.123m), ImporteAsiento));
    }

    [Fact]
    public void IvaTieneQueCerrarConNetoPorAlicuota()
    {
        AsientosService.ValidarLibroIva(Libro(l => { l.RetencionIibb = 0; l.NetoGravado = 1000m; l.Alicuota = 21m; l.Iva = 210m; }), ImporteAsiento);
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => { l.RetencionIibb = 0; l.NetoGravado = 1000m; l.Alicuota = 21m; l.Iva = 200m; }), ImporteAsiento));
    }

    [Fact]
    public void IvaSinNetoOSinAlicuotaValidaNoPasa()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => { l.RetencionIibb = 0; l.Iva = 210m; l.Alicuota = 21m; }), ImporteAsiento));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => { l.RetencionIibb = 0; l.NetoGravado = 1000m; l.Alicuota = 20m; l.Iva = 200m; }), ImporteAsiento));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => { l.RetencionIibb = 0; l.NetoGravado = 1000m; }), ImporteAsiento));
    }

    [Fact]
    public void ElLibroTieneQueSerComprasOVentas()
        => Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarLibroIva(Libro(l => l.Libro = "OTRO"), ImporteAsiento));
}
