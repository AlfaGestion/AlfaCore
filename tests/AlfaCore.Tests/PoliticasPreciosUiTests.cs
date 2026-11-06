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

public sealed class PoliticasPreciosUiTests
{
    [Fact]
    public async Task EditorMuestraPoliticasValidaFormulasYGraba()
    {
        var porcentaje = new PoliticaPrecioDto { CodigoOriginal = "   1", Codigo = "   1", Descripcion = "MAYORISTA", Porcentajes = [0, 0, 0, 30, 25, 0, 0, 0], Articulos = 12 };
        var formulas = new PoliticaPrecioDto { CodigoOriginal = "   2", Codigo = "   2", Descripcion = "CON IVA" };
        formulas.Formulas[3] = "<COSTO>%+30*1.21";
        PoliticaPrecioDto? grabada = null;
        var svc = DispatchProxy.Create<IPoliticasPreciosService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)svc).Handler = (m, args) => m.Name switch
        {
            "GetContextoAsync" => Task.FromResult(new PoliticaPrecioContextoDto
            {
                Politicas = [porcentaje, formulas], PuedeEditar = true,
                NombresClases = ["PROVEEDOR", "FLETE", "SEGURO", "VENTA", "OFERTA", "", "", ""]
            }),
            "GuardarAsync" => Guardar((PoliticaPrecioDto)args![0]!),
            _ => null
        };
        Task<PoliticaPrecioDto> Guardar(PoliticaPrecioDto p) { grabada = p; return Task.FromResult(p); }

        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(svc);
        services.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime, CargaComprobantesUiTests.EventOnlyProxy>());
        var activator = new Captura(); services.AddSingleton<IComponentActivator>(activator);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<PoliticasPreciosEditor>();
            var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("MAYORISTA", html);
            Assert.Contains("VENTA +30% · OFERTA +25%", html);
            Assert.Contains("Fórmulas: VENTA = <COSTO>%+30*1.21", html);
            var editor = activator.Editor!;
            Assert.Contains(editor.Acciones, a => a.Key == "pp-nueva" && !a.Disabled);

            // Nueva política con fórmulas: la fórmula mal escrita muestra qué corregir y el ejemplo de las válidas.
            await editor.Acciones.Single(a => a.Key == "pp-nueva").OnClick!();
            Invocar(editor, "CambiarModo", "F");
            var form = (PoliticaPrecioDto)typeof(PoliticasPreciosEditor).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            Assert.Equal("3", form.Codigo);
            form.Descripcion = "LISTA";
            form.Formulas[3] = "<COSTO> %+ 50";
            form.Formulas[4] = "COSTO*2";
            Invocar(editor, "StateHasChanged");
            html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Ejemplo: 1.500,00", html);
            Assert.Contains("tiene que empezar con una variable", html);

            form.Formulas[4] = "<CLASE4>%-10";
            await editor.Acciones.Single(a => a.Key == "pp-grabar").OnClick!();
            Assert.NotNull(grabada);
            Assert.True(grabada!.UsaFormulas);
            Assert.Equal("<COSTO> %+ 50", grabada.Formulas[3]);
        });
    }

    private static void Invocar(object c, string metodo, params object?[] args)
        => c.GetType().GetMethod(metodo, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            args.Select(a => a?.GetType() ?? typeof(string)).ToArray(), null)!.Invoke(c, args);

    private sealed class Captura : IComponentActivator
    {
        public PoliticasPreciosEditor? Editor { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var c = (IComponent)Activator.CreateInstance(type)!;
            if (c is PoliticasPreciosEditor e) Editor = e;
            return c;
        }
    }
}
