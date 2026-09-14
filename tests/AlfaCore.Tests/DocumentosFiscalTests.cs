using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class DocumentosFiscalTests
{
    [Theory]
    [InlineData(1, "B", "A", "001")]
    [InlineData(6, "A", "B", "006")]
    [InlineData(11, "A", "C", "011")]
    [InlineData(null, " a ", "A", "001")]
    public void TipoElectronicoTienePrioridad(int? tipo, string letra, string esperada, string codigo)
    {
        Assert.Equal((esperada, codigo), FacturaDocumentService.ResolveLetraYCodigo(tipo, letra));
    }

    [Theory]
    [InlineData(51)]
    [InlineData(999)]
    public void NoConvierteOtrosTiposEnFactura(int tipo)
    {
        Assert.Null(FacturaDocumentService.ResolveLetraYCodigo(tipo, "A").Letra);
    }

    [Theory]
    [InlineData(2, "A", "002", "Nota de débito")]
    [InlineData(3, "A", "003", "Nota de crédito")]
    [InlineData(7, "B", "007", "Nota de débito")]
    [InlineData(8, "B", "008", "Nota de crédito")]
    [InlineData(12, "C", "012", "Nota de débito")]
    [InlineData(13, "C", "013", "Nota de crédito")]
    public void NotasConservanTipoLetraYTitulo(int codigo, string letra, string textoCodigo, string nombre)
    {
        Assert.Equal((letra, textoCodigo), FacturaDocumentService.ResolveLetraYCodigo(codigo, "X"));
        var tipo = TiposDocumentoCore.Fiscal(codigo)!;
        Assert.Equal(nombre, tipo.Nombre);
        var html = new DocumentRenderer().RenderFactura(DocumentTemplateDefinition.CrearFacturaEstandar(letra),
            new FacturaDocumentData { Comprobante = new() { TipoDocumento = tipo.Tipo, Letra = letra, CodigoAfip = textoCodigo } });
        Assert.Contains(System.Net.WebUtility.HtmlEncode(nombre.ToUpperInvariant()), html);
        Assert.DoesNotContain("<b>FACTURA ", html);
        Assert.Contains(textoCodigo, html);
    }

    [Fact]
    public void QrRespetaTamanoYAlineacionEnHtmlCompartido()
    {
        var template = DocumentTemplateDefinition.CrearFacturaEstandar("A");
        var qr = template.Blocks.Single(x => x.Type == TiposBloqueDocumento.QrAfip);
        qr.Width = 35.5m;
        qr.Align = "right";
        var html = new DocumentRenderer().RenderFactura(template, new FacturaDocumentData { QrBytes = [1, 2, 3] });
        Assert.Contains("width:35.5mm;height:35.5mm", html);
        Assert.Contains("class=\"qr-box\" style=\"text-align:right", html);
        Assert.Contains("data:image/png;base64,AQID", html);
        Assert.DoesNotContain("QR fiscal no disponible", html);
    }

    [Fact]
    public void CaeSinQrMuestraAvisoInclusoSiBloqueEstaOculto()
    {
        var template = DocumentTemplateDefinition.CrearFacturaEstandar("A");
        template.Blocks.Single(x => x.Type == TiposBloqueDocumento.QrAfip).Visible = false;
        var html = new DocumentRenderer().RenderFactura(template, new FacturaDocumentData
        {
            Cae = new FacturaCaeDocumentData { Cae = "12345678901234", Resultado = "A" }
        });
        Assert.Contains("QR fiscal no disponible", html);
        Assert.DoesNotContain("alt=\"QR fiscal ARCA\"", html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(61)]
    public void RechazaTamanoQrFueraDeRango(int width)
    {
        var template = DocumentTemplateDefinition.CrearFacturaEstandar("A");
        template.Blocks.Single(x => x.Type == TiposBloqueDocumento.QrAfip).Width = width;
        var service = new DocumentTemplateService(null!, null!, null!, null!);
        Assert.Throws<InvalidOperationException>(() => service.DeserializeAndValidate(
            JsonSerializer.Serialize(template), TiposDocumentoCore.FacturaA));
    }

    [Fact]
    public void PlantillaAnteriorSinTamanoUsaTreintaMilimetros()
    {
        var template = DocumentTemplateDefinition.CrearFacturaEstandar("A");
        template.Blocks.Single(x => x.Type == TiposBloqueDocumento.QrAfip).Width = null;
        var service = new DocumentTemplateService(null!, null!, null!, null!);
        var validated = service.DeserializeAndValidate(JsonSerializer.Serialize(template), TiposDocumentoCore.FacturaA);
        var html = new DocumentRenderer().RenderFactura(validated, new FacturaDocumentData { QrBytes = [1] });
        Assert.Contains("width:30mm;height:30mm", html);
    }
}
