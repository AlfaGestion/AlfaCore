using System.Reflection;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ListasPreciosUiTests
{
    [Fact]
    public async Task GrillaEditaClasesPropiasYMuestraLasDelMaestroSinEditar()
    {
        var fila = new ListaPrecioFilaDto
        {
            Id = 7, IdArticulo = "207", Descripcion = "ACEITE", Rubro = "ALMACEN", Costo = 1000m, TasaIva = 21m, ConIva = true,
            Precio1 = 1000m, Precio2 = 1100m, Precio3 = 1200m, Precio4 = 2420m, ClasesDelMaestro = "0123",
            OfertaDesde = DateTime.Today.AddDays(-1), OfertaHasta = DateTime.Today.AddDays(5), Precio5 = 2000m, CantidadOf2 = 6, Precio9 = 10
        };
        var lista = new ListaPrecioDto { IdLista = "0001", Nombre = "AUTOSERVICIO", ConIva = true, Articulos = 1, ClasesDelMaestro = "0123", Decimales = 2 };
        ListaPrecioFilaDto? guardada = null;
        var svc = DispatchProxy.Create<IListasPreciosService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)svc).Handler = (m, args) => m.Name switch
        {
            "GetContextoAsync" => Task.FromResult(new ListaPrecioContextoDto
            {
                NombresClases = ["PROVEEDOR", "FLETE", "SEGURO", "VENTA", "OFERTA", "", "", ""], ClaseVenta = 4, ClaseOferta = 5,
                Retail = true, OfertaPorDescuento = true, Listas = [lista]
            }),
            "BuscarAsync" => Task.FromResult(new ListaPrecioPaginaDto { Lista = lista, Filas = [fila], Total = 1 }),
            "GuardarFilaAsync" => Guardar((ListaPrecioFilaDto)args![1]!),
            _ => null
        };
        Task<ListaPrecioFilaDto> Guardar(ListaPrecioFilaDto f) { guardada = f; return Task.FromResult(f); }

        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(svc);
        services.AddSingleton<NavigationManager, CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton<IRouteContextService, RouteContextService>();
        var modo = DispatchProxy.Create<IAppModeService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)modo).Handler = (m, _) => m.Name == "get_IsSaaSMode" ? true : null;
        services.AddSingleton(modo);
        services.AddSingleton<ISessionService>(DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton<IAppUserSessionService>(DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton<IPageHeaderService, PageHeaderService>();
        services.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime, CargaComprobantesUiTests.EventOnlyProxy>());
        var activator = new Captura(); services.AddSingleton<IComponentActivator>(activator);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<ArticulosListasPrecios>();
            var header = provider.GetRequiredService<IPageHeaderService>();
            Assert.Equal("Listas de precios", header.Current.Title);
            Assert.Contains(header.Current.Actions, x => x.Key == "nueva");
            var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("AUTOSERVICIO", html);
            Assert.Contains("Del maestro: costo, PROVEEDOR, FLETE, SEGURO", html);
            Assert.Contains("aria-label=\"VENTA de 207\"", html);      // clase propia: editable
            Assert.DoesNotContain("aria-label=\"PROVEEDOR de 207\"", html); // del maestro: solo lectura
            Assert.Contains("≥6: 10%", html);                          // precio por cantidad como % (OfertaPorDescuento)
            Assert.Contains("Vigente", html);
            Assert.Contains("Margen", html);

            var page = activator.Page!;
            var cambiar = typeof(ArticulosListasPrecios).GetMethod("CambiarPrecioAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)cambiar.Invoke(page, [fila, 4, "2.500,50"])!;
            Assert.NotNull(guardada);
            Assert.Equal(2500.50m, guardada!.Precio4);
            Assert.Equal(2420m, fila.Precio4); // la fila original no se toca: se reemplaza por la grabada
        });
    }

    private sealed class Captura : IComponentActivator
    {
        public ArticulosListasPrecios? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var c = (IComponent)Activator.CreateInstance(type)!;
            if (c is ArticulosListasPrecios p) Page = p;
            return c;
        }
    }
}
