using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraDocumentoVisorTests
{
    private static T SinConsultas<T>() where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=(m,_)=>throw new InvalidOperationException("No debe consultar sin el alcance autorizado: "+m.Name);
        return p;
    }
    [Fact]
    public async Task VisorUsaLaCompraAutorizadaYElAsientoCompletoSinConsultarPorNumeroSolo()
    {
        var servicios=new ServiceCollection();servicios.AddLogging();
        servicios.AddSingleton(SinConsultas<IComprobanteViewerService>());servicios.AddSingleton(SinConsultas<IFacturaDocumentService>());
        servicios.AddSingleton(SinConsultas<ICompanyBrandingService>());servicios.AddSingleton(SinConsultas<IRouteContextService>());
        servicios.AddSingleton(SinConsultas<IJSRuntime>());servicios.AddSingleton<NavigationManager>(new CargaComprobantesUiTests.TestNavigation());
        servicios.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        var datos=new ComprobanteViewerDto{Cabecera=new(){Tc="FCC",IdComprobante="000100000002A",SistemaOrigen="COMPRAS",Nombre="Proveedor de ensayo",Cuenta="211010002",ImporteTotal=12.1m,Fecha=new(2026,8,25)},TieneAsiento=true,
            Asientos=[new(){Cuenta="211010002",DescripcionCuenta="Proveedor de ensayo",Haber=12.1m,NumeroAsiento=54},new(){Cuenta="11203",DescripcionCuenta="IVA crédito fiscal",Debe=2.1m,NumeroAsiento=54},new(){Cuenta="13101",DescripcionCuenta="Gastos",Debe=10,NumeroAsiento=54}]};
        using var provider=servicios.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<ComprobanteViewer>(ParameterView.FromDictionary(new Dictionary<string,object?>
        {{"DatosConsulta",datos},{"Tc","FCC"},{"IdComprobante","000100000002A"},{"ModoDetalleCompleto",true},{"AbrirAsientoInicial",true}})));
        var html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Detalle del asiento contable",html);Assert.Contains("211010002",html);Assert.Contains("11203",html);Assert.Contains("13101",html);
        Assert.DoesNotContain("Abrir en otra pestaña",html);Assert.DoesNotContain("Ver PDF",html);
    }
}
