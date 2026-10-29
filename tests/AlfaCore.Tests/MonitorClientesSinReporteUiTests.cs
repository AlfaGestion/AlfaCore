using System.Reflection;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace AlfaCore.Tests;

public sealed class MonitorClientesSinReporteUiTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsultaDesdeBarraMuestraCargaYResultadoOError(bool falla)
    {
        var completar = new TaskCompletionSource<IReadOnlyList<MonitorClienteSinReporteDto>>();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(Proxy<IAppUserSessionService>((m, _) => m.Name == "get_CurrentUser"
            ? new AppUserSessionInfo { SuperAdmin = true } : null));
        services.AddSingleton(Proxy<IMonitorClientesService>((m, _) => m.Name switch
        {
            "get_Disponible" => true,
            "GetClientesSinReporteAsync" => completar.Task,
            _ => null
        }));
        services.AddSingleton<IPageHeaderService, PageHeaderService>();
        services.AddSingleton(Proxy<IRouteContextService>((_, _) => null));
        services.AddSingleton<NavigationManager, CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton(Proxy<IJSRuntime>((_, _) => null));
        var captura = new Captura(); services.AddSingleton<IComponentActivator>(captura);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<PaginaSinCarga>());
        Task? carga = null;
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            typeof(AdminMonitorClientes).GetMethod("UpdatePageHeader", Flags)!.Invoke(captura.Page, null);
            carga = provider.GetRequiredService<IPageHeaderService>().Current.Actions.Single(a => a.Key == "sin-reporte").OnClick!();
            Assert.Contains("Consultando clientes sin reporte", root.ToHtmlString());
            Assert.Contains("role=\"dialog\"", root.ToHtmlString());
        });
        if (falla) completar.SetException(new InvalidOperationException("Fallo de consulta de prueba"));
        else completar.SetResult(Enumerable.Range(1, 52).Select(i => new MonitorClienteSinReporteDto
        {
            ClienteCodigo = i.ToString(), RazonSocial = $"Cliente demo {i:D3}",
            Clasificacion = i % 2 == 0 ? "1" : "3", ClasificacionDescripcion = "Clasificación demo"
        }).ToList());
        await carga!;
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.DoesNotContain("Consultando clientes sin reporte", html);
            if (falla)
            {
                Assert.Contains("No se pudo cargar la lista", html);
                Assert.Contains("Fallo de consulta de prueba", html);
                Assert.DoesNotContain("Sin clientes pendientes con estos filtros", html);
                return;
            }
            Assert.Contains("52 de 52 clientes", html);
            Assert.Contains("Cliente demo 050", html);
            Assert.DoesNotContain("Cliente demo 051", html);
            Assert.Contains("P1 y P2", html);
            Assert.Contains("Sin clasificación", html);
            typeof(AdminMonitorClientes).GetField("_clasificacionSinReporte", Flags)!.SetValue(captura.Page, "1,2");
            typeof(ComponentBase).GetMethod("StateHasChanged", Flags)!.Invoke(captura.Page, null);
            html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("26 de 52 clientes", html);
            Assert.Contains("Cliente demo 002", html);
            Assert.DoesNotContain("Cliente demo 001", html);
        });
    }

    public sealed class PaginaSinCarga : AdminMonitorClientes
    {
        protected override void OnInitialized() { }
        protected override Task OnInitializedAsync() => Task.CompletedTask;
        protected override void OnAfterRender(bool firstRender) { }
    }
    private sealed class Captura : IComponentActivator
    {
        public PaginaSinCarga? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is PaginaSinCarga page) Page = page;
            return component;
        }
    }
}
