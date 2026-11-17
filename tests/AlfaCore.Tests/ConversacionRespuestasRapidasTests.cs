using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConversacionRespuestasRapidasTests
{
    private static readonly ConversacionRespuestaRapidaDto[] Respuestas =
    [
        new() { IdRespuesta = 1, Atajo = "INFO:", Texto = "¡Hola! Para comprar minorista entrá a la tienda online." },
        new() { IdRespuesta = 2, Atajo = "24hs", Texto = "Te escribo porque ayer te enviamos el presupuesto." },
        new() { IdRespuesta = 3, Atajo = "48hs", Texto = "Esta semana tenemos 10% de descuento." },
        new() { IdRespuesta = 4, Atajo = "infomayo", Texto = "Tenemos dos maneras de trabajar." },
        new() { IdRespuesta = 5, Atajo = "remito", Texto = "Con remito no cobramos IVA." },
    ];

    [Theory]
    [InlineData("info", "info")]
    [InlineData("::info", "info")]
    [InlineData("  :: info mayo ", "infomayo")]
    [InlineData("INFO:", "INFO:")]
    [InlineData(null, "")]
    public void NormalizarAtajo_SacaDisparadorYEspacios(string? atajo, string esperado)
        => Assert.Equal(esperado, ConversacionRespuestasRapidas.NormalizarAtajo(atajo));

    [Fact]
    public void NormalizarAtajo_RecortaAlMaximo()
        => Assert.Equal(ConversacionRespuestasRapidas.LargoMaximoAtajo, ConversacionRespuestasRapidas.NormalizarAtajo(new string('a', 80)).Length);

    [Fact]
    public void Filtrar_SinConsulta_DevuelveTodasOrdenadas()
    {
        var resultado = ConversacionRespuestasRapidas.Filtrar(Respuestas, string.Empty);

        Assert.Equal(["24hs", "48hs", "INFO:", "infomayo", "remito"], resultado.Select(r => r.Atajo));
    }

    [Fact]
    public void Filtrar_PrimeroLasQueEmpiezanConLaConsultaSinImportarMayusculas()
    {
        var resultado = ConversacionRespuestasRapidas.Filtrar(Respuestas, "info");

        Assert.Equal(["INFO:", "infomayo"], resultado.Select(r => r.Atajo));
    }

    [Fact]
    public void Filtrar_DespuesLasQueLoTienenEnElAtajoYAlFinalEnElTexto()
    {
        var resultado = ConversacionRespuestasRapidas.Filtrar(Respuestas, "hs");
        Assert.Equal(["24hs", "48hs"], resultado.Select(r => r.Atajo));

        var porTexto = ConversacionRespuestasRapidas.Filtrar(Respuestas, "iva");
        Assert.Equal(["remito"], porTexto.Select(r => r.Atajo));
    }

    [Fact]
    public void Filtrar_SinCoincidencias_DevuelveVacio()
        => Assert.Empty(ConversacionRespuestasRapidas.Filtrar(Respuestas, "zzz"));

    [Fact]
    public void Filtrar_NoMuestraMasDelMaximo()
    {
        var muchas = Enumerable.Range(1, 20).Select(i => new ConversacionRespuestaRapidaDto { IdRespuesta = i, Atajo = $"r{i:00}", Texto = "x" });

        Assert.Equal(ConversacionRespuestasRapidas.MaximoSugerencias, ConversacionRespuestasRapidas.Filtrar(muchas, "r").Count);
    }
}
