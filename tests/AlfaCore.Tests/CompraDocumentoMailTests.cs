using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraDocumentoMailTests
{
    private static readonly EmpresaDocumentData Empresa = new() { Nombre = "ALFA NET <SRL>", Cuit = "30-11111111-1", Domicilio = "Calle 1" };

    private static CompraDocumentoMailService.Ficha Pago() => new()
    {
        Tc = "PG", Comprobante = "000100000223X", Fecha = new DateTime(2026, 8, 6), Cuenta = "211010002", Nombre = "CARGILL & CIA", Cuit = "30-22222222-2",
        Importe = 889000m, RetGanancias = 42000m, RetIva = 147000m
    };

    [Fact]
    public void LasMarcasDelMailSeCompletanConLosDatosDelDocumento()
    {
        var texto = CompraDocumentoMailService.ArmarTexto("<NOMBRE>|<COMPROBANTE>|<FECHA>|<impuesto>", " CARGILL ", "000100000223X", new DateTime(2026, 8, 6), 10m, CompraCertificado.Iva);
        Assert.Equal("CARGILL|0001-00000223-X|06/08/2026|IVA", texto);
    }

    [Theory]
    [InlineData("OC", "", "OC")]
    [InlineData("RMC", "", "OC")]
    [InlineData("PG", "", "PAGO")]
    [InlineData("OPG", "", "PAGO")]
    [InlineData("PG", "GAN", "RETENCION")]
    public void CadaDocumentoUsaSuPropioTextoConfigurable(string tc, string certificado, string esperado)
        => Assert.Equal(esperado, CompraDocumentoMailService.ClaseTexto(tc, certificado));

    [Fact]
    public void ElCertificadoMuestraLoRetenidoYEscapaLosNombres()
    {
        var retencion = new CompraDocumentoMailService.Retencion
        {
            Certificado = CompraCertificado.Ganancias, Numero = "14", Regimen = "2 · RET. GANANCIAS", Base = 700000m, Alicuota = 6m, Importe = 42000m, Pago = Pago()
        };
        var html = CompraDocumentoMailService.HtmlCertificado(Empresa, retencion);
        Assert.Contains("CERTIFICADO DE RETENCI", html); // la «Ó» sale como entidad HTML: el PDF la dibuja igual
        Assert.Contains("Impuesto a las Ganancias", html);
        Assert.Contains("42.000,00", html);
        Assert.Contains("700.000,00", html);
        Assert.Contains("CARGILL &amp; CIA", html);
        Assert.Contains("ALFA NET &lt;SRL&gt;", html);
        Assert.DoesNotContain("CARGILL & CIA", html);
        Assert.Contains("0001-00000223-X", html);
    }

    [Fact]
    public void ElPagoListaLasRetencionesPracticadasYElTotal()
    {
        var pago = Pago();
        var retenciones = new[]
        {
            new CompraDocumentoMailService.Retencion { Certificado = CompraCertificado.Ganancias, Numero = "14", Base = 700000m, Alicuota = 6m, Importe = 42000m, Pago = pago },
            new CompraDocumentoMailService.Retencion { Certificado = CompraCertificado.Iva, Numero = "16", Base = 700000m, Alicuota = 21m, Importe = 147000m, Pago = pago }
        };
        var html = CompraDocumentoMailService.HtmlPago(Empresa, pago, retenciones, null);
        Assert.Contains("COMPROBANTE DE PAGO", html);
        Assert.Contains("Retenciones practicadas", html);
        Assert.Contains("Impuesto a las Ganancias", html);
        Assert.Contains("147.000,00", html);
        Assert.Contains("889.000,00", html);
    }

    [Fact]
    public void LaOrdenDePagoSeTitulaComoTal()
    {
        var pago = Pago(); pago.Tc = "OPG";
        Assert.Contains("ORDEN DE PAGO", CompraDocumentoMailService.HtmlPago(Empresa, pago, [], null));
    }

    [Fact]
    public void LaOrdenDeCompraListaLosArticulosYElTotal()
    {
        var ficha = new CompraDocumentoMailService.Ficha { Tc = "OC", Comprobante = "000100000007X", Fecha = new DateTime(2026, 11, 3), Cuenta = "211010001", Nombre = "Proveedor", Importe = 121m };
        var dto = new ComprobanteViewerDto
        {
            Cabecera = new ComprobanteCabeceraDto { ImporteTotal = 121m, ImporteSinIva = 100m, Iva = 21m },
            Insumos = [new ComprobanteInsumoDto { CodigoArticulo = "87", Descripcion = "Tornillo <M8>", Unidad = "UN", Cantidad = 10m, PrecioImporte = 10m, Total = 100m }]
        };
        var html = CompraDocumentoMailService.HtmlOrdenCompra(Empresa, ficha, dto);
        Assert.Contains("ORDEN DE COMPRA", html);
        Assert.Contains("Tornillo &lt;M8&gt;", html);
        Assert.Contains("121,00", html);
        Assert.Contains("0001-00000007-X", html);
    }

    [Fact]
    public void ElCertificadoSoloExisteParaLosCuatroImpuestos()
    {
        Assert.Equal(["GAN", "IVA", "IBR", "SUSS"], CompraCertificado.Todos);
        Assert.All(CompraCertificado.Todos, c => Assert.False(string.IsNullOrWhiteSpace(CompraCertificado.Nombre(c))));
        Assert.All(CompraCertificado.Todos, c => Assert.StartsWith("RET", CompraCertificado.Prefijo(c)));
    }
}
