using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ComprasIdentidadFiscalTests
{
    [Fact]
    public async Task VerDeDosFilasConElMismoNumeroEnviaLaCuentaDeCadaFila()
    {
        var visor = new ComprobanteVerService(); visor.Changed += () => { };
        var captura = new CapturaAccion();
        using var provider = new ServiceCollection().AddLogging().AddSingleton(visor).AddSingleton<IComponentActivator>(captura).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            foreach (var cuenta in new[] { "211010520", "211010693" })
            {
                await renderer.RenderComponentAsync<ComprobanteVerAccion>(ParameterView.FromDictionary(new Dictionary<string, object?> {
                    ["Tc"] = "FCC", ["Comprobante"] = "001100021200A", ["Cuenta"] = cuenta }));
                typeof(ComprobanteVerAccion).GetMethod("Abrir", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(captura.Actual, null);
                Assert.Equal(cuenta, visor.Actual!.Cuenta);
            }
        });
    }

    private sealed class CapturaAccion : IComponentActivator
    {
        public ComprobanteVerAccion? Actual { get; private set; }
        public IComponent CreateInstance(Type tipo)
        {
            var componente = (IComponent)Activator.CreateInstance(tipo)!;
            if (componente is ComprobanteVerAccion accion) Actual = accion;
            return componente;
        }
    }

    [Theory]
    [InlineData("211010520")]
    [InlineData("211010693")]
    public async Task ElVisorPropagaLaCuentaAlComprobanteYAlAsiento(string cuenta)
    {
        T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var p = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler = handler;
            return p;
        }
        var llamadas = new List<string>();
        var servicios = new ServiceCollection().AddLogging();
        servicios.AddSingleton(Proxy<IComprobanteViewerService>((m, a) =>
        {
            if (m.Name == "GetAsync")
            {
                Assert.Equal(cuenta, a![4]); llamadas.Add("comprobante");
                return Task.FromResult<ComprobanteViewerDto?>(new() { Cabecera = new() { Tc = "FCC", IdComprobante = "001100021200A",
                    SistemaOrigen = "COMPRAS", Cuenta = cuenta, Nombre = "Proveedor", Fecha = new(2026, 10, 21) }, TieneAsiento = true });
            }
            Assert.Equal("GetAsientosAsync", m.Name); Assert.Equal(cuenta, a![3]); llamadas.Add("asiento");
            return Task.FromResult<IReadOnlyList<ComprobanteAsientoDto>>([new() { Cuenta = cuenta, Haber = 121 },
                new() { Cuenta = "13104", Debe = 100 }, new() { Cuenta = "11203", Debe = 21 }]);
        }));
        servicios.AddSingleton(Proxy<ICompanyBrandingService>((_, _) => Task.FromResult(new CompanyBrandingDto())));
        servicios.AddSingleton(Proxy<IFacturaDocumentService>((_, _) => null));
        servicios.AddSingleton(Proxy<IRouteContextService>((m, a) => m.Name == "BuildRoute" ? a![0] : null));
        servicios.AddSingleton(Proxy<IJSRuntime>((_, _) => null));
        servicios.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        servicios.AddSingleton<NavigationManager, CargaComprobantesUiTests.TestNavigation>();
        using var provider = servicios.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<ComprobanteViewer>(ParameterView.FromDictionary(new Dictionary<string, object?> {
                ["Tc"] = "FCC", ["IdComprobante"] = "001100021200A", ["Cuenta"] = cuenta, ["AbrirAsientoInicial"] = true, ["ModoDetalleCompleto"] = true }));
            var html = root.ToHtmlString();
            Assert.Contains(cuenta, html);
            Assert.Contains("13104", html); Assert.Contains("11203", html);
            Assert.Contains("cuenta=" + cuenta, html);
            Assert.Equal(new[] { "comprobante", "asiento" }, llamadas);
        });
    }
}

/// <summary>Consulta solamente los duplicados ya existentes; no importa ni modifica movimientos.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class ComprasIdentidadFiscalFernandezTests
{
    [FernandezFact]
    public async Task AbreCadaCuentaConSuAsientoYBuscaDuplicadosPorCuitAunqueCambieElProveedor()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("FERNANDEZF2", cn.Database);
        var visor = new ComprobanteViewerService(runtime.Configuration, runtime.Sessions, runtime.Events);
        foreach (var (cuenta, numeroAsiento) in new[] { ("211010520", 3177), ("211010693", 3831) })
        {
            var r = await visor.GetAsync("FCC", "001100021200A", cuenta: cuenta);
            Assert.NotNull(r?.Cabecera); Assert.Equal(cuenta, r.Cabecera.Cuenta.Trim());
            var asiento = await visor.GetAsientosAsync("FCC", "001100021200A", cuenta: cuenta);
            Assert.Equal(3, asiento.Count);
            Assert.All(asiento, l => Assert.Equal(numeroAsiento, l.NumeroAsiento));
            Assert.Equal(0, asiento.Sum(l => l.Debe - l.Haber));
        }
        Assert.Null(await visor.GetAsync("FCC", "001100021200A", cuenta: "211099999"));
        var a = new ArcaMisComprobante { Clase = "FC", Letra = "A", PuntoVenta = 11, NumeroDesde = 21200, NumeroHasta = 21200,
            DocContraparte = "30-70753649-3" };
        foreach (var proveedor in new string?[] { null, "211010693", "211099999" })
            Assert.Equal("211010520", await ControlArcaImportacionService.CuentaExistenteAsync(cn, null, a, proveedor, "FCC", default));
        a.DocContraparte = "30709390356";
        Assert.Null(await ControlArcaImportacionService.CuentaExistenteAsync(cn, null, a, null, "FCC", default));
        a.DocContraparte = "30707536493"; a.Letra = "B";
        Assert.Null(await ControlArcaImportacionService.CuentaExistenteAsync(cn, null, a, null, "FCC", default));
    }
}
