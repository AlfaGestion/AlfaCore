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

public sealed class ComprobanteAsientoDirectoTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccesoDirectoYAccesoDesdeComprobanteUsanElMismoAsientoCompleto(bool directo)
    {
        T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
        {
            var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
        }
        var dto=new ComprobanteViewerDto { TieneAsiento=true,
            Cabecera=new(){Tc="FC",IdComprobante="000200003828A",SistemaOrigen="VENTAS",Nombre="CLIENTE DE PRUEBA",Fecha=new(2026,9,1)},
            Acciones=[new(){TipoAccion="ALTA",Comentario="Registro original"}] };
        IReadOnlyList<ComprobanteAsientoDto> lineas=[
            new(){Cuenta="112010951",DescripcionCuenta="CLIENTE DE PRUEBA",Debe=194803.33m,NumeroAsiento=18,UnidadNegocio="2"},
            new(){Cuenta="21201",DescripcionCuenta="IVA DEBITO",Haber=33808.84m},
            new(){Cuenta="43101",DescripcionCuenta="VENTAS",Haber=160994.49m}];
        var consultas=0;var cerrados=0;
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(Proxy<IComprobanteViewerService>((m,a)=>m.Name switch {
            "GetAsync"=>Task.FromResult<ComprobanteViewerDto?>(dto),
            "GetAsientosAsync"=>Cargar(a!),_=>throw new InvalidOperationException(m.Name) }));
        Task<IReadOnlyList<ComprobanteAsientoDto>> Cargar(object?[] a)
        {
            Assert.Equal("FC",a[0]);Assert.Equal("000200003828A",a[1]);consultas++;return Task.FromResult(lineas);
        }
        services.AddSingleton(Proxy<ICompanyBrandingService>((_,_)=>Task.FromResult(new CompanyBrandingDto())));
        services.AddSingleton(Proxy<IFacturaDocumentService>((_,_)=>null));
        services.AddSingleton(Proxy<IRouteContextService>((m,a)=>m.Name=="BuildRoute" ? a![0] : null));
        services.AddSingleton(Proxy<IJSRuntime>((_,_)=>null));
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        var capture=new Capture();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var root=await renderer.RenderComponentAsync<ComprobanteViewer>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Tc"]="FC",["IdComprobante"]="000200003828A",["ModoDetalleCompleto"]=true,["AbrirAsientoInicial"]=directo,
                ["AsientoCerrado"]=EventCallback.Factory.Create(new object(),()=>cerrados++) }));
            Assert.Equal(directo ? 1 : 0,consultas);
            if (!directo)
            {
                Assert.DoesNotContain("Detalle del asiento contable",root.ToHtmlString());
                await (Task)typeof(ComprobanteViewer).GetMethod("MostrarAsientoAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.Viewer,null)!;
                typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.Viewer,null);
            }
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Detalle del asiento contable",html);
            Assert.Contains("112010951",html);Assert.Contains("21201",html);Assert.Contains("43101",html);
            Assert.Contains("Suma de débitos",html);Assert.Contains("Suma de créditos",html);
            Assert.Contains("Historial de acciones",html);Assert.Contains("Registro original",html);
            Assert.Equal(1,consultas);
            await (Task)typeof(ComprobanteViewer).GetMethod("CerrarAsientoModalAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.Viewer,null)!;
            Assert.Equal(1,cerrados);
        });
    }
    private sealed class Capture:IComponentActivator
    {
        public ComprobanteViewer? Viewer {get;private set;}
        public IComponent CreateInstance(Type type) {var instance=(IComponent)Activator.CreateInstance(type)!;if(instance is ComprobanteViewer viewer)Viewer=viewer;return instance;}
    }
}
