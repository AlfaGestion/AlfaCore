using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class DocumentEmisorTests
{
    [Fact]
    public void UnidadElectronicaDefineRazonSocialYCuitDelEncabezadoYQr()
    {
        var emisor = FacturaDocumentService.ResolveEmisor(true, " Empresa unidad ", " 30-00000000-7 ",
            "Empresa general", "20111111112", "20222222223");
        Assert.Equal("Empresa unidad", emisor.Nombre);
        Assert.Equal("30-00000000-7", emisor.Cuit);
        Assert.Equal(emisor.Cuit, emisor.CuitQr);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Empresa unidad", "30000000007")]
    public void SinMarcaElectronicaUsaConfiguracionGeneral(string? nombreUnidad, string? cuitUnidad)
    {
        var emisor = FacturaDocumentService.ResolveEmisor(false, nombreUnidad, cuitUnidad,
            " Empresa general ", " 20111111112 ", " 20222222223 ");
        Assert.Equal(("Empresa general", "20111111112", "20222222223"), emisor);
    }

    [Theory]
    [InlineData("", "30000000007")]
    [InlineData("Empresa unidad", null)]
    public void UnidadElectronicaIncompletaNoSeReemplazaPorOtraEmpresa(string nombre, string? cuit)
    {
        Assert.Throws<ArgumentException>(() => FacturaDocumentService.ResolveEmisor(true, nombre, cuit,
            "Empresa general", "20111111112", "20222222223"));
    }

    [Theory]
    [InlineData("A4", TiposDocumentoCore.FacturaA)]
    [InlineData("Ticket80", TiposDocumentoCore.CreditoB)]
    [InlineData("A4", TiposDocumentoCore.DebitoC)]
    public void FormatosFiscalesMuestranElEmisorResuelto(string papel, string tipo)
    {
        var emisor = FacturaDocumentService.ResolveEmisor(true, "Sucursal <Uno>", "30000000007",
            "Empresa general", "20111111112", "20222222223");
        var definition = DocumentTemplateDefinition.CrearFacturaEstandar(TiposDocumentoCore.LetraDe(tipo)!);
        definition.Paper.Size = papel;
        definition.Blocks.Single(b => b.Type == TiposBloqueDocumento.Pie).Visible = true;
        var html = new DocumentRenderer().RenderFactura(definition, new FacturaDocumentData
        {
            Empresa = new() { Nombre = emisor.Nombre, Cuit = emisor.Cuit },
            Comprobante = new() { TipoDocumento = tipo, Letra = TiposDocumentoCore.LetraDe(tipo)! }
        });
        Assert.Contains("Sucursal &lt;Uno&gt;", html);
        Assert.Contains("30000000007", html);
        Assert.DoesNotContain("Empresa general", html);
        if (papel == "Ticket80") Assert.Contains("ticket-footer\">Sucursal &lt;Uno&gt;", html);
    }

    // A4/Carta: CUIT, IIBB e inicio de actividades a la derecha bajo el número; ticket: columna única bajo la empresa.
    [Theory]
    [InlineData("A4", false)]
    [InlineData("Ticket80", true)]
    public void DatosFiscalesDelEmisorVanBajoElNumeroSalvoEnTicket(string papel, bool ticket)
    {
        var definition = DocumentTemplateDefinition.CrearFacturaEstandar("C");
        definition.Paper.Size = papel;
        var html = new DocumentRenderer().RenderFactura(definition, new FacturaDocumentData
        {
            Empresa = new() { Nombre = "Emisor", Cuit = "27228073259" },
            IngresosBrutosEmisor = "902-819418-9",
            InicioActividadesEmisor = new DateTime(2020, 1, 1),
            Comprobante = new() { TipoDocumento = TiposDocumentoCore.FacturaC, Letra = "C" }
        });
        var meta = html.IndexOf("fiscal-emisor fiscal-emisor--meta", StringComparison.Ordinal);
        Assert.True(meta > html.IndexOf("class=\"doc-meta\"", StringComparison.Ordinal));
        Assert.Contains("CUIT: 27228073259", html[meta..]);
        Assert.Contains("Inicio de Actividades: 01/01/2020", html[meta..]);
        Assert.Contains("fiscal-emisor--empresa{display:none}", html);
        Assert.Equal(ticket, html.Contains("fiscal-emisor--meta{display:none!important}", StringComparison.Ordinal));
    }
}
