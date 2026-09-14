using System.Globalization;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ArcaQrTests
{
    private static ArcaQrData Example => new(new DateTime(2020, 10, 13), "30-00000000-7", 10, 1, 94,
        12100m, "DOL", 65m, 80, "20-00000000-1", "E", "70417054367476");

    [Fact]
    public void PayloadCoincideConEjemploArcaInclusoEnCulturaArgentina()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
            var url = ArcaQrService.BuildUrl(Example);
            Assert.StartsWith("https://www.arca.gob.ar/fe/qr/?p=", url);
            using var payload = JsonDocument.Parse(Convert.FromBase64String(url.Split("?p=")[1]));
            var json = payload.RootElement;
            Assert.Equal(1, json.GetProperty("ver").GetInt32());
            Assert.Equal("2020-10-13", json.GetProperty("fecha").GetString());
            Assert.Equal(30000000007m, json.GetProperty("cuit").GetDecimal());
            Assert.Equal(10, json.GetProperty("ptoVta").GetInt32());
            Assert.Equal(1, json.GetProperty("tipoCmp").GetInt32());
            Assert.Equal(94, json.GetProperty("nroCmp").GetInt32());
            Assert.Equal(12100m, json.GetProperty("importe").GetDecimal());
            Assert.Equal("DOL", json.GetProperty("moneda").GetString());
            Assert.Equal(65m, json.GetProperty("ctz").GetDecimal());
            Assert.Equal(80, json.GetProperty("tipoDocRec").GetInt32());
            Assert.Equal(20000000001m, json.GetProperty("nroDocRec").GetDecimal());
            Assert.Equal("E", json.GetProperty("tipoCodAut").GetString());
            Assert.Equal(70417054367476m, json.GetProperty("codAut").GetDecimal());
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void GeneraPngSinArchivosNiDatosLegacy()
    {
        var png = new ArcaQrService().GeneratePng(Example);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.True(png.Length > 100);
    }

    [Fact]
    public void ConservaDocumentoReceptorDeVeinteDigitosYCaEa()
    {
        var url = ArcaQrService.BuildUrl(Example with { NumeroDocumentoReceptor = "12345678901234567890", TipoAutorizacion = "A" });
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(url.Split("?p=")[1]));
        Assert.Contains("\"nroDocRec\":12345678901234567890", json);
        Assert.Contains("\"tipoCodAut\":\"A\"", json);
    }

    [Fact]
    public void RechazaDatosIncompletosSinInventarValores()
    {
        var invalid = new[]
        {
            Example with { Cuit = "" }, Example with { Autorizacion = "123" },
            Example with { PuntoVenta = 0 }, Example with { Numero = 100000000 },
            Example with { Fecha = default }, Example with { Cotizacion = 0 },
            Example with { Importe = -1 }, Example with { Importe = 1.001m },
            Example with { Moneda = "PES", Cotizacion = 65 }, Example with { Moneda = "XXX" },
            Example with { TipoAutorizacion = "CAE" }, Example with { NumeroDocumentoReceptor = "<script>" },
            Example with { TipoDocumentoReceptor = null }
        };
        foreach (var data in invalid) Assert.Throws<ArgumentException>(() => ArcaQrService.BuildUrl(data));
    }

    [Theory]
    [InlineData(null, "PES")]
    [InlineData("0", "PES")]
    [InlineData(" 1", "PES")]
    [InlineData("2", "DOL")]
    [InlineData("3", "EUR")]
    public void EquivalenciasDeMonedaConfirmadas(string? codigo, string expected)
        => Assert.Equal(expected, FacturaDocumentService.ResolveMonedaQr(codigo));

    [Fact]
    public void NoConvierteMonedaDesconocidaEnDolares()
        => Assert.Throws<ArgumentException>(() => FacturaDocumentService.ResolveMonedaQr("4"));
}
