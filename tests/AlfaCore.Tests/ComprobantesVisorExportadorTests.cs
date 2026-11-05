using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Xunit;

namespace AlfaCore.Tests;

public class ComprobantesVisorExportadorTests
{
    [Fact]
    public void ExcelConservaCodigosTextoEImportesNumericos()
    {
        var bytes = ComprobantesVisorExportador.Excel([new() { Tc = "FC", Cuenta = "00123", Cliente = "=SUM(A1)", Importe = 1234.56m }]);
        using var libro = new XLWorkbook(new MemoryStream(bytes));
        var hoja = libro.Worksheet(1);
        Assert.Equal("00123", hoja.Cell(2, 4).GetString());
        Assert.False(hoja.Cell(2, 5).HasFormula);
        Assert.Equal(1234.56m, hoja.Cell(2, 6).GetValue<decimal>());
    }

    [Theory]
    [InlineData("0010-00000519 C", "001000000519C")]
    [InlineData("10-519 C", "001000000519C")]
    [InlineData("001000000519C", "001000000519C")]
    public void VisorRecibeElNumeroInternoDesdeLosReportes(string visible, string interno)
        => Assert.Equal(interno, AlfaCore.Components.Shared.ComprobanteVerAccion.NormalizarNumero(visible));

    [Fact]
    public void PdfEscapaTextoProvenienteDeLaBase()
    {
        var html = ComprobantesVisorExportador.Html([new() { Cliente = "<script>alert(1)</script>" }], new());
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
