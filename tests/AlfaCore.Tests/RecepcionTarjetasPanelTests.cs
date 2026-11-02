using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlfaCore.Tests;

public sealed class RecepcionTarjetasPanelTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }
    [Theory]
    [InlineData("CUENTA",false,"Aplicar cuenta y continuar")]
    [InlineData("ERROR",false,"Reintentar")]
    [InlineData("LISTO",false,"Revisar en Conciliación de tarjetas")]
    [InlineData("LISTO",true,"Listo para revisar")]
    public async Task MuestraAccionesSegunEstadoYRespetaElModoDirecto(string estado,bool directo,string esperado)
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var sesiones=Proxy<ISessionService>((_,_)=>null);var usuarios=Proxy<IAppUserSessionService>((m,_)=>m.ReturnType==typeof(bool)?false:null);
        var permisos=Proxy<IPermissionService>((_,_)=>Task.FromResult(true));
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(sesiones);services.AddSingleton(usuarios);
        services.AddSingleton(new ConciliacionTarjetasService(sesiones,usuarios,permisos,runtime.Events,null!));
        services.AddSingleton(new TarjetasRecepcionService(sesiones,usuarios,permisos,runtime.Events,new ConfigurationBuilder().Build(),null!,null!,null!));
        services.AddSingleton(Proxy<IInterfacesService>((_,_)=>null));services.AddSingleton(runtime.Events);
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton<IRouteContextService>(Proxy<IRouteContextService>((_,a)=>a?[0]));
        services.AddSingleton<NavigationManager>(new Navegacion(directo));
        var captura=new Captura();services.AddSingleton<IComponentActivator>(captura);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<RecepcionTarjetasPanel>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"DocumentoId",1L}})));
        await renderer.Dispatcher.InvokeAsync(()=>
        {
            typeof(RecepcionTarjetasPanel).GetField("_trabajo",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(captura.Panel,new TarjetasRecepcionTrabajo{DocumentoId=1,Estado=estado,Mensaje="Preparado sin cambiar asientos",ImportacionId=estado=="LISTO"?123:null,AnalisisId=8});
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(captura.Panel,null);
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains(esperado,html);Assert.Contains("Preparado sin cambiar asientos",html);
            if(directo)Assert.DoesNotContain("Revisar en Conciliación de tarjetas",html);
        });
    }
    private sealed class Navegacion:NavigationManager
    {
        public Navegacion(bool directo)=>Initialize("https://localhost/","https://localhost/interfaces/1"+(directo?"?directo=1":""));
        protected override void NavigateToCore(string uri,bool forceLoad){}
    }
    private sealed class Captura:IComponentActivator
    {
        public RecepcionTarjetasPanel? Panel {get;private set;}
        public IComponent CreateInstance(Type type){var c=(IComponent)Activator.CreateInstance(type)!;if(c is RecepcionTarjetasPanel p)Panel=p;return c;}
    }
}
