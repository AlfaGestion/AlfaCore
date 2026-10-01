using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Regresión Base4264 (2026-10-01): con "Mostrar precios a consumidores finales / leads" desactivado
/// (CATALOGO_MUESTRA_PRECIO_CONSUMIDOR=0) el link público del catálogo que recibe un lead mostraba
/// precios igual: CatalogoPublico.razor y el PDF público nunca leían esa clave.
/// </summary>
public sealed class CatalogoPublicoPreciosVisibilityTests
{
    private static CatalogosCatalogoDetalleDto Catalogo() => new()
    {
        IdInsert = 1,
        Nombre = "prueba",
        Articulos =
        [
            new CatalogosCatalogoItemDto { IdArticulo = "01", DescripcionArticulo = "PILA DURACELL - AA", Precio = 2500m, PrecioOferta = 2300m, PrecioOfertaAnterior = 2500m, PrecioOfertaNuevo = 2300m },
            new CatalogosCatalogoItemDto { IdArticulo = "02", DescripcionArticulo = "PILA DURACELL - AAA", Precio = 2000m }
        ]
    };

    [Fact]
    public void PreciosOff_VisitanteAnonimo_CatalogoSinNingunPrecioEnElDto()
    {
        var original = Catalogo();

        var publico = CatalogosPublicPriceVisibility.ParaVisitante(original, muestraPreciosConsumidorFinal: false, visitanteAutenticado: false);

        Assert.False(publico.PreciosVisibles);
        Assert.Equal(2, publico.Articulos.Count);
        Assert.Equal(["PILA DURACELL - AA", "PILA DURACELL - AAA"], publico.Articulos.Select(a => a.DescripcionArticulo));
        Assert.All(publico.Articulos, a =>
        {
            Assert.Null(a.Precio);
            Assert.Null(a.PrecioOferta);
            Assert.Null(a.PrecioOfertaAnterior);
            Assert.Null(a.PrecioOfertaNuevo);
        });
    }

    [Fact]
    public void PreciosOff_NoModificaElCatalogoOriginalCacheado()
    {
        var original = Catalogo();

        _ = CatalogosPublicPriceVisibility.SinPrecios(original);

        Assert.True(original.PreciosVisibles);
        Assert.Equal(2500m, original.Articulos[0].Precio);
        Assert.Equal(2300m, original.Articulos[0].PrecioOferta);
    }

    [Fact]
    public void PreciosOn_MantieneExactamenteElCatalogo()
    {
        var original = Catalogo();

        var publico = CatalogosPublicPriceVisibility.ParaVisitante(original, muestraPreciosConsumidorFinal: true, visitanteAutenticado: false);

        Assert.Same(original, publico);
        Assert.True(publico.PreciosVisibles);
        Assert.Equal(2500m, publico.Articulos[0].Precio);
    }

    [Fact]
    public void PreciosOff_ClienteLogueadoEnElCatalogo_ConservaPrecios()
    {
        var original = Catalogo();

        var publico = CatalogosPublicPriceVisibility.ParaVisitante(original, muestraPreciosConsumidorFinal: false, visitanteAutenticado: true);

        Assert.Same(original, publico);
    }

    [Fact]
    public void PreciosOff_DtoSerializadoNoFiltraPrecios()
    {
        var publico = CatalogosPublicPriceVisibility.SinPrecios(Catalogo());

        var json = System.Text.Json.JsonSerializer.Serialize(publico);

        Assert.DoesNotContain("2500", json);
        Assert.DoesNotContain("2300", json);
        Assert.DoesNotContain("2000", json);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData(" 0 ", false)]
    [InlineData("NO", false)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("SI", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void InterpretarMuestraPrecioConsumidor_SoloUnApagadoExplicitoOcultaPrecios(string? valor, bool esperado)
        => Assert.Equal(esperado, InterfacesCatalogosService.InterpretarMuestraPrecioConsumidor(valor));

    /// <summary>Todas las salidas públicas anónimas pasan por la visibilidad de precios; el carrito
    /// (cliente autenticado) y la confirmación de pedido no.</summary>
    [Fact]
    public void Source_SalidasPublicasAplicanVisibilidad_CarritoNo()
    {
        var root = FindRepositoryRoot();
        var pagina = File.ReadAllText(Path.Combine(root, "src", "AlfaCore", "Components", "Pages", "CatalogoPublico.razor"));
        var program = File.ReadAllText(Path.Combine(root, "src", "AlfaCore", "Program.cs"));
        var carrito = File.ReadAllText(Path.Combine(root, "src", "AlfaCore", "Services", "CarritoComprasService.cs"));

        Assert.Contains("MuestraPreciosConsumidorFinalAsync", pagina);
        Assert.Contains("CatalogosPublicPriceVisibility.ParaVisitante", pagina);
        var pdf = program.IndexOf("private static async Task<IResult> GenerarPdfCatalogoAsync(", StringComparison.Ordinal);
        Assert.True(pdf > 0);
        var cuerpoPdf = program[pdf..program.IndexOf("return Results.File(pdfBytes", pdf, StringComparison.Ordinal)];
        Assert.Contains("MuestraPreciosConsumidorFinalAsync", cuerpoPdf);
        Assert.Contains("CatalogosPublicPriceVisibility.ParaVisitante", cuerpoPdf);
        Assert.DoesNotContain("CatalogosPublicPriceVisibility", carrito);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
