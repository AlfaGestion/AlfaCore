using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlfaCore.Tests;

public sealed class VentasAnalisisTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DrilldownConservaFiltrosIncluyeSinAsignarYPagina(bool vendedor)
    {
        var llamadas = new List<(VentasDashboardFilters Filtros, bool Vendedor, string? Codigo, int Pagina)>();
        var svc = DispatchProxy.Create<IGestionDashboardService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)svc).Handler = (m, a) => m.Name switch
        {
            "GetVentasFilterOptionsAsync" => Task.FromResult(new VentasFilterOptionsDto()),
            "GetVentasAnalisisAsync" => Consultar(a!),
            _ => null
        };
        Task<VentasAnalisisDto> Consultar(object?[] a)
        {
            llamadas.Add(((VentasDashboardFilters)a[0]!, (bool)a[1]!, (string?)a[2], (int)a[3]!));
            return Task.FromResult(new VentasAnalisisDto
            {
                Filas = [new() { Codigo = "", Nombre = "Sin asignar", Total = -50, Comprobantes = 51 }],
                CantidadComprobantes = a[2] is null ? 0 : 51,
                Evolucion = a[2] is null ? [] : [new() { Periodo = "2026-10", Total = -50 }],
                Comprobantes = a[2] is null ? [] : [new() { Tc = "NC", IdComprobante = "000100000001A", Cliente = "Cliente", Total = -50 }]
            });
        }
        var servicios = new ServiceCollection(); servicios.AddLogging();
        servicios.AddSingleton(svc);
        servicios.AddSingleton<GestionFilterStateService>();
        servicios.AddSingleton<IPageHeaderService, PageHeaderService>();
        servicios.AddSingleton<NavigationManager, CargaComprobantesUiTests.TestNavigation>();
        var routes = DispatchProxy.Create<IRouteContextService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)routes).Handler = (_, a) => "/ALFANET/115" + a![0];
        servicios.AddSingleton(routes);
        var activador = new Captura(); servicios.AddSingleton<IComponentActivator>(activador);
        using var provider = servicios.BuildServiceProvider();
        var filtros = provider.GetRequiredService<GestionFilterStateService>().Ventas;
        filtros.UnidadesNegocio = ["2"]; filtros.Usuario = "OPERADOR"; filtros.Deposito = "1";
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<VentasAnalisis>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(VentasAnalisis.PorVendedor)] = vendedor }));
            Assert.Contains("Sin asignar", root.ToHtmlString());
            Assert.Null(llamadas.Last().Codigo);
            async Task Invoke(string nombre, params object[] args)
            {
                await (Task)typeof(VentasAnalisis).GetMethod(nombre, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(activador.Page, args)!;
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(activador.Page, null);
            }
            await Invoke("SeleccionarAsync", "");
            Assert.Equal("", llamadas.Last().Codigo);
            Assert.Contains("Ver comprobante", root.ToHtmlString());
            await Invoke("CambiarPaginaAsync", 1);
            Assert.Equal(2, llamadas.Last().Pagina);
            Assert.Equal("", llamadas.Last().Codigo);
            await Invoke("AplicarAsync");
            Assert.Equal(1, llamadas.Last().Pagina);
            Assert.All(llamadas, x => { Assert.Same(filtros, x.Filtros); Assert.Equal(vendedor, x.Vendedor); });
            await Invoke("CerrarDetalleAsync");
            Assert.Null(llamadas.Last().Codigo);
            var header = provider.GetRequiredService<IPageHeaderService>().Current;
            Assert.Equal("ventas", header.ModuleKey);
            Assert.True(Assert.Single(header.TopNavigationItems, x => x.Key == (vendedor ? "vendedores" : "unidades-negocio")).Active);
        });
    }

    private sealed class Captura : IComponentActivator
    {
        public VentasAnalisis? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var componente = (IComponent)Activator.CreateInstance(type)!;
            if (componente is VentasAnalisis page) Page = page;
            return componente;
        }
    }
}
