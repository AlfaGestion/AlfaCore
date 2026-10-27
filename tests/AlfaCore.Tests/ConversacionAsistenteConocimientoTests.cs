using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Información del negocio del asistente (2026-10-05): bloques por tema y archivos en un vector store
/// de OpenAI propio de cada base.
/// </summary>
public sealed class ConversacionAsistenteConocimientoTests
{
    [Fact]
    public void ComponerInformacion_SumaGeneralBloquesActivosPorTemaYFragmentos()
    {
        var bloques = new[]
        {
            new AsistenteBloqueDto { IdBloque = 2, Categoria = "PAGOS", Titulo = "Tarjetas", Contenido = "Visa y Master en 3 cuotas.", Orden = 20 },
            new AsistenteBloqueDto { IdBloque = 1, Categoria = "HORARIOS", Titulo = "Sucursal centro", Contenido = "Lunes a viernes de 9 a 18.", Orden = 10 },
            new AsistenteBloqueDto { IdBloque = 3, Categoria = "PAGOS", Titulo = "Viejo", Contenido = "No usar.", Activo = false }
        };
        var fragmentos = new[] { new AsistenteFragmentoDto("manual.pdf", 0.9, "Para facturar entrá a Ventas.") };

        var texto = ConversacionAsistenteConocimientoService.ComponerInformacion("Somos AlfaNet.", bloques, fragmentos);

        Assert.StartsWith("Somos AlfaNet.", texto);
        Assert.True(texto.IndexOf("## Horarios y sucursales", StringComparison.Ordinal) < texto.IndexOf("## Medios de pago", StringComparison.Ordinal));
        Assert.Contains("### Tarjetas", texto);
        Assert.DoesNotContain("No usar.", texto);
        Assert.Contains("[manual.pdf]", texto);
        Assert.Contains("Para facturar entrá a Ventas.", texto);
    }

    [Fact]
    public void ComponerInformacion_SinBloquesNiFragmentosDevuelveLaGeneral()
        => Assert.Equal("Somos AlfaNet.", ConversacionAsistenteConocimientoService.ComponerInformacion(" Somos AlfaNet. ", [], []));

    [Fact]
    public void ComponerInformacion_LimitaElTamanoDeLosFragmentos()
    {
        var largo = new string('x', ConversacionAsistenteConocimientoService.CaracteresMaximosFragmentos);
        var texto = ConversacionAsistenteConocimientoService.ComponerInformacion(string.Empty, [],
            [new AsistenteFragmentoDto("a.pdf", 1, largo), new AsistenteFragmentoDto("b.pdf", 0.5, "no entra")]);

        Assert.Contains("[a.pdf]", texto);
        Assert.DoesNotContain("[b.pdf]", texto);
    }

    [Theory]
    [InlineData("manual.pdf", 1000, null)]
    [InlineData("lista.DOCX", 1000, null)]
    [InlineData("foto.jpg", 1000, "Formato no admitido")]
    [InlineData("vacio.txt", 0, "vacío")]
    [InlineData("grande.pdf", 21 * 1024 * 1024, "supera el máximo")]
    [InlineData("", 10, "no tiene nombre")]
    public void ValidarArchivo(string nombre, long tamano, string? errorEsperado)
    {
        var error = ConversacionAsistenteConocimientoService.ValidarArchivo(nombre, tamano);
        if (errorEsperado is null)
            Assert.Null(error);
        else
            Assert.Contains(errorEsperado, error);
    }

    [Fact]
    public void LeerResultadosBusqueda_TomaArchivoPuntajeYTexto()
    {
        const string json = """
            {"object":"vector_store.search_results.page","data":[
              {"file_id":"file-1","filename":"envios.pdf","score":0.82,"content":[{"type":"text","text":"Enviamos a todo el país."}]},
              {"file_id":"file-2","filename":"vacio.pdf","score":0.1,"content":[]}
            ]}
            """;

        var fragmento = Assert.Single(ConversacionAsistenteConocimientoService.LeerResultadosBusqueda(json));
        Assert.Equal("envios.pdf", fragmento.Archivo);
        Assert.Equal(0.82, fragmento.Puntaje, 2);
        Assert.Equal("Enviamos a todo el país.", fragmento.Texto);
    }

    [Theory]
    [InlineData("""{"id":"vs_1","metadata":{"alfacore_base":"4264"}}""", "4264", true)]
    [InlineData("""{"id":"vs_1","metadata":{"alfacore_base":"84"}}""", "4264", false)]
    [InlineData("""{"id":"vs_1","metadata":{}}""", "4264", false)]
    [InlineData("""{"id":"vs_1"}""", "4264", false)]
    [InlineData("""{"id":"vs_1","metadata":{"alfacore_base":""}}""", "", false)]
    public void PerteneceALaBase_SoloSiLaMetadataCoincide(string json, string claveBase, bool esperado)
        => Assert.Equal(esperado, ConversacionAsistenteConocimientoService.PerteneceALaBase(json, claveBase));

    [Theory]
    [InlineData("""{"status":"completed"}""", AsistenteArchivoEstados.Listo, "")]
    [InlineData("""{"status":"in_progress"}""", AsistenteArchivoEstados.Procesando, "")]
    [InlineData("""{"status":"failed","last_error":{"code":"unsupported_file","message":"File type not supported"}}""", AsistenteArchivoEstados.Error, "File type not supported")]
    [InlineData("""{"status":"cancelled"}""", AsistenteArchivoEstados.Error, "OpenAI no pudo procesar el archivo.")]
    public void MapearEstado_DesdeOpenAi(string json, string estado, string error)
        => Assert.Equal((estado, error), ConversacionAsistenteConocimientoService.MapearEstado(json));

    [Theory]
    [InlineData("pagos", AsistenteBloqueCategorias.Pagos)]
    [InlineData("cualquiera", AsistenteBloqueCategorias.General)]
    [InlineData(null, AsistenteBloqueCategorias.General)]
    public void NormalizarCategoria(string? valor, string esperado)
        => Assert.Equal(esperado, ConversacionAsistenteConocimientoService.NormalizarCategoria(valor));

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(3 * 1024 * 1024, "3 MB")]
    public void FormatearTamano(long bytes, string esperado)
        => Assert.Equal(esperado, ConversacionesConfiguracionAsistenteConocimiento.FormatearTamano(bytes).Replace(',', '.'));
}
