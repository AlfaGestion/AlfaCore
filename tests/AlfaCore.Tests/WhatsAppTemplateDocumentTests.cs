using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Plantillas con encabezado DOCUMENT (Cierre de caja → WhatsApp con la ventana de 24 h vencida):
/// solo se aceptan cuando el llamador trae el archivo; sin archivo siguen siendo no soportadas.
/// </summary>
public sealed class WhatsAppTemplateDocumentTests
{
    private const string ComponentesConDocumento =
        """[{"type":"HEADER","format":"DOCUMENT"},{"type":"BODY","text":"Hola {{1}}, te enviamos el cierre de caja del {{2}}."}]""";

    private const string ComponentesSoloTexto =
        """[{"type":"HEADER","format":"TEXT","text":"Aviso"},{"type":"BODY","text":"Hola {{1}}."}]""";

    private static ConversacionPlantillaDto Plantilla(string componentes, string cuerpo) => new()
    {
        IdPlantilla = 1,
        NombreMeta = "cierre_de_caja",
        CuerpoTexto = cuerpo,
        ComponentesMetaJson = componentes,
        EstadoMeta = "APPROVED",
        Activa = true
    };

    [Fact]
    public void RequiereDocumento_DetectaHeaderDocument()
    {
        Assert.True(WhatsAppTemplateValidation.RequiereDocumento(Plantilla(ComponentesConDocumento, "Hola {{1}}, cierre del {{2}}.")));
        Assert.False(WhatsAppTemplateValidation.RequiereDocumento(Plantilla(ComponentesSoloTexto, "Hola {{1}}.")));
        Assert.False(WhatsAppTemplateValidation.RequiereDocumento(Plantilla(string.Empty, "Hola.")));
        Assert.False(WhatsAppTemplateValidation.RequiereDocumento(Plantilla("no es json", "Hola.")));
    }

    [Fact]
    public void HeaderDocument_SinArchivo_SigueSiendoNoSoportado()
    {
        var plantilla = Plantilla(ComponentesConDocumento, "Hola {{1}}, cierre del {{2}}.");

        Assert.NotEmpty(WhatsAppTemplateValidation.UnsupportedReason(plantilla));
        Assert.Throws<InvalidOperationException>(() => WhatsAppTemplateValidation.ValidateSend(plantilla, ["Ana", "08/10/2026"]));
    }

    [Fact]
    public void HeaderDocument_ConArchivo_SeAceptaYValidaVariables()
    {
        var plantilla = Plantilla(ComponentesConDocumento, "Hola {{1}}, cierre del {{2}}.");

        Assert.Empty(WhatsAppTemplateValidation.UnsupportedReason(plantilla, documentoDisponible: true));
        WhatsAppTemplateValidation.ValidateSend(plantilla, ["Ana", "08/10/2026"], documentoDisponible: true);
        Assert.Throws<InvalidOperationException>(() =>
            WhatsAppTemplateValidation.ValidateSend(plantilla, ["Ana"], documentoDisponible: true));
    }

    [Fact]
    public void ConArchivo_PlantillaSinHeaderDocument_SeRechaza()
    {
        var plantilla = Plantilla(ComponentesSoloTexto, "Hola {{1}}.");

        Assert.Throws<InvalidOperationException>(() =>
            WhatsAppTemplateValidation.ValidateSend(plantilla, ["Ana"], documentoDisponible: true));
    }

    [Fact]
    public void PlantillaLocalConEncabezadoDocumento_RequiereDocumentoSinPayloadDeMeta()
    {
        var plantilla = Plantilla(string.Empty, "Hola {{1}}, cierre del {{2}}.");
        plantilla.EncabezadoFormato = ConversacionPlantillaEncabezados.Documento;

        Assert.True(WhatsAppTemplateValidation.RequiereDocumento(plantilla));
        // Sin archivo (envío común desde Conversaciones) se rechaza con un mensaje claro.
        Assert.NotEmpty(WhatsAppTemplateValidation.UnsupportedReason(plantilla));
        WhatsAppTemplateValidation.ValidateSend(plantilla, ["Ana", "08/10/2026"], documentoDisponible: true);
    }

    [Theory]
    [InlineData(null, "TEXT")]
    [InlineData("", "TEXT")]
    [InlineData("text", "TEXT")]
    [InlineData("document", "DOCUMENT")]
    [InlineData(" DOCUMENT ", "DOCUMENT")]
    [InlineData("IMAGE", "TEXT")]
    public void EncabezadoFormato_SeNormaliza(string? valor, string esperado)
        => Assert.Equal(esperado, ConversacionPlantillaEncabezados.Normalizar(valor));

    [Fact]
    public void PdfDeEjemplo_EsUnPdfValidoConXrefCorrecto()
    {
        var bytes = ConversacionesService.BuildTemplateSamplePdf();
        var texto = System.Text.Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", texto, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", texto, StringComparison.Ordinal);

        // startxref apunta exactamente a la tabla xref, y cada entrada al "N 0 obj" correspondiente.
        var startxref = int.Parse(texto[(texto.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.Equal("xref", texto.Substring(startxref, 4));
        var entradas = texto[startxref..].Split('\n').Skip(3).Take(5).ToArray();
        for (var i = 0; i < entradas.Length; i++)
        {
            var offset = int.Parse(entradas[i][..10]);
            Assert.StartsWith($"{i + 1} 0 obj", texto[offset..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PayloadDeCreacion_ConEncabezadoDocumento_LlevaHeaderHandle()
    {
        var plantilla = Plantilla(string.Empty, "Hola {{1}}, cierre del {{2}}.");
        plantilla.EncabezadoFormato = ConversacionPlantillaEncabezados.Documento;
        plantilla.Categoria = ConversacionPlantillaCategorias.Utility;
        plantilla.Idioma = "es_AR";

        var method = typeof(ConversacionesService).GetMethod("BuildMetaTemplateCreatePayload",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var payload = (Dictionary<string, object?>)method.Invoke(null, [plantilla, "4::handle"])!;
        var json = System.Text.Json.JsonSerializer.Serialize(payload);

        Assert.Contains("\"format\":\"DOCUMENT\"", json, StringComparison.Ordinal);
        Assert.Contains("\"header_handle\":[\"4::handle\"]", json, StringComparison.Ordinal);

        // Sin handle no se puede armar: Meta rechazaría la plantilla.
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [plantilla, null]));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }
}
