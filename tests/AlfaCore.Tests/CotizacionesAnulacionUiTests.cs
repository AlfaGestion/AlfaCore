using System.Reflection;
using AlfaCore.Components.Pages;
using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CotizacionesAnulacionUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnularDesdeToolbarPideConfirmacionYRespetaSeleccion(bool confirmar)
    {
        var anuladas = new List<long>();
        var services = new ServiceCollection();
        services.AddLogging();
        var cotizaciones = DispatchProxy.Create<ICotizacionesService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)cotizaciones).Handler = (method, args) => method.Name switch
        {
            "GetListAsync" => Task.FromResult(new PagedResult<CotizacionListItemDto>
            {
                Items = new long[] { 30, 31, 32 }.Select(id => new CotizacionListItemDto { IdCotizacion = id }).ToList(),
                Total = 3, PageNumber = 1, PageSize = 50
            }),
            "GetResumenAsync" => Task.FromResult(new CotizacionResumenDto()),
            "AnularAsync" => Anular((long)args![0]!),
            _ => throw new InvalidOperationException($"Llamada inesperada: {method.Name}")
        };
        Task Anular(long id) { anuladas.Add(id); return Task.CompletedTask; }
        services.AddSingleton(cotizaciones);
        services.AddSingleton<IPageHeaderService, PageHeaderService>();
        services.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        services.AddSingleton(DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton(DispatchProxy.Create<IJSRuntime, CargaComprobantesUiTests.EventOnlyProxy>());
        var routes = DispatchProxy.Create<IRouteContextService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)routes).Handler = (_, args) => args![0];
        services.AddSingleton(routes);
        services.AddSingleton<NavigationManager, CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var activator = new CapturingActivator();
        services.AddSingleton<IComponentActivator>(activator);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<Cotizaciones>();
            var page = activator.Page!;
            foreach (var id in new long[] { 30, 32 })
                await (Task)typeof(Cotizaciones).GetMethod("ToggleRowSelectionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(page, [id, new ChangeEventArgs { Value = true }])!;

            var header = provider.GetRequiredService<IPageHeaderService>();
            await header.Current.Actions.Single(a => a.Key == "anular-masiva").OnClick!();
            // El callback pertenece al toolbar: no hay un evento de la página que la vuelva a renderizar.
            Assert.Contains("role=\"dialog\"", root.ToHtmlString());
            var dialog = activator.Dialogs.Single(d => d.Open);
            Assert.Contains("2 cotización(es)", dialog.Message);
            Assert.Empty(anuladas);

            if (confirmar)
                await dialog.OnConfirm.InvokeAsync();
            else
                await dialog.OnCancel.InvokeAsync();

            Assert.DoesNotContain("role=\"dialog\"", root.ToHtmlString());
            Assert.Equal(confirmar ? new long[] { 30, 32 } : [], anuladas.Order().ToArray());
            Assert.Equal(!confirmar, header.Current.Actions.Any(a => a.Key == "anular-masiva"));
        });
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public Cotizaciones? Page { get; private set; }
        public List<AlfaConfirmDialog> Dialogs { get; } = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is Cotizaciones page) Page = page;
            if (component is AlfaConfirmDialog dialog) Dialogs.Add(dialog);
            return component;
        }
    }
}
