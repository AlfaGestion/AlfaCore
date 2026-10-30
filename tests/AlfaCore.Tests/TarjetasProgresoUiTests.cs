using System.Reflection;
using AlfaCore.Components.Pages;
using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasProgresoUiTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }
    [Theory]
    [InlineData("exito")]
    [InlineData("error")]
    [InlineData("cancelacion")]
    public async Task ProgresoSeRenderizaAntesDeEsperarYSeRetiraAlTerminar(string resultado)
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var sessions=Proxy<ISessionService>((_,_)=>null);var users=Proxy<IAppUserSessionService>((_,_)=>null);
        var service=new ConciliacionTarjetasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);services.AddSingleton(sessions);services.AddSingleton(users);
        services.AddSingleton(runtime.Events);services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton(Proxy<IRouteContextService>((_,_)=>null));services.AddSingleton(Proxy<IJSRuntime>((_,_)=>null));
        var capture=new Captura();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var header=provider.GetRequiredService<IPageHeaderService>();
        var inicio=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completar=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? operacion=null;
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PaginaSinCarga>());
        await renderer.Dispatcher.InvokeAsync(()=>
        {
            Func<CancellationToken,Task> trabajo=_=>{inicio.SetResult();return completar.Task;};
            operacion=(Task)typeof(ConciliacionTarjetas).GetMethod("Operar",Flags)!.Invoke(capture.Page,
                [trabajo,AppUiMessage.InProgress("Analizando con IA…","Buscando coincidencias; esperá el resultado."),"ia"])!;
        });
        await inicio.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await renderer.Dispatcher.InvokeAsync(()=>
        {
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Analizando con IA…",html);Assert.Contains("Buscando coincidencias",html);
            Assert.Contains("role=\"status\"",html);Assert.Contains("aria-live=\"polite\"",html);
            Assert.Contains("alfa-feedback-panel__icon--spin",html);
            var ia=header.Current.Actions.Single(x=>x.Key=="ia");
            Assert.True(ia.Busy);Assert.True(ia.Disabled);Assert.Equal("Analizando con IA…",ia.Label);
            Assert.All(header.Current.Actions,a=>Assert.True(a.Disabled));
        });
        if(resultado=="error")completar.SetException(new InvalidOperationException("Fallo de ensayo"));
        else if(resultado=="cancelacion")completar.SetCanceled();else completar.SetResult();
        await operacion!.WaitAsync(TimeSpan.FromSeconds(5));
        await renderer.Dispatcher.InvokeAsync(()=>
        {
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.DoesNotContain("Buscando coincidencias",html);Assert.DoesNotContain("tarjetas-progreso",html);
            var ia=header.Current.Actions.Single(x=>x.Key=="ia");Assert.False(ia.Busy);Assert.False(ia.Disabled);Assert.Equal("Asistir con IA",ia.Label);
            Assert.Contains("misma fecha e importe",header.Current.Actions.Single(x=>x.Key=="proponer").Title);
            if(resultado=="error")Assert.NotNull(typeof(ConciliacionTarjetas).GetField("_feedback",Flags)!.GetValue(capture.Page));
        });
    }
    [Theory]
    [InlineData(false,false,"Sin pendientes para proponer")]
    [InlineData(true,false,"Sin coincidencias únicas")]
    [InlineData(true,true,"Selección propuesta")]
    public async Task ProponerDesdeLaBarraGlobalRenderizaSiempreSuResultado(bool datos,bool coincide,string titulo)
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var sessions=Proxy<ISessionService>((_,_)=>null);var users=Proxy<IAppUserSessionService>((_,_)=>null);
        var service=new ConciliacionTarjetasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);services.AddSingleton(sessions);services.AddSingleton(users);
        services.AddSingleton(runtime.Events);services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton(Proxy<IRouteContextService>((_,_)=>null));services.AddSingleton(Proxy<IJSRuntime>((_,_)=>null));
        var capture=new Captura();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PaginaSinCarga>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            typeof(ConciliacionTarjetas).GetField("_comparacion",Flags)!.SetValue(capture.Page,new TarjetasComparacion(new(){Id=1,DetalleCompleto=true},
                datos?[new(){Id=1,Fecha=new(2026,4,1),Importe=100}]:[],
                datos?[new(){Clave="A",Fecha=new(2026,4,1),Importe=coincide?100:200}]:[],1));
            typeof(ConciliacionTarjetas).GetMethod("Header",Flags)!.Invoke(capture.Page,[]);
            var header=provider.GetRequiredService<IPageHeaderService>();
            await header.Current.Actions.Single(a=>a.Key=="proponer").OnClick!();
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains(titulo,html);Assert.Contains("role=\"status\"",html);Assert.Contains("alfa-feedback-panel",html);
            if(datos&&coincide)
            {
                Assert.DoesNotContain("Confirmar conciliación de tarjetas",html);
                var conciliar=header.Current.Actions.Single(a=>a.Key=="conciliar");
                Assert.False(conciliar.Disabled);
                await conciliar.OnClick!();
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("Confirmar conciliación de tarjetas",html);
                Assert.Contains("Confirmá después de revisar el original",html);
            }
        });
    }
    [Fact]
    public async Task DeshacerAbreLaVentanaAunqueFalleConsultaYConservaErrorAlDescartarNotificacion()
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var sessions=Proxy<ISessionService>((_,_)=>null);
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name=="get_IsAuthenticated"?false:null);
        var service=new ConciliacionTarjetasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);services.AddSingleton(sessions);services.AddSingleton(users);
        services.AddSingleton(runtime.Events);services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton(Proxy<IRouteContextService>((_,_)=>null));services.AddSingleton(Proxy<IJSRuntime>((_,_)=>null));
        var capture=new Captura();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PaginaSinCarga>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            await (Task)typeof(ConciliacionTarjetas).GetMethod("AbrirDeshacerVarias",Flags)!.Invoke(capture.Page,null)!;
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Deshacer conciliaciones de tarjetas",html);
            Assert.Contains("Iniciá sesión en la base donde querés conciliar",html);
            Assert.Contains("Volver a consultar",html);
            Assert.NotNull(typeof(ConciliacionTarjetas).GetField("_errorDeshacer",Flags)!.GetValue(capture.Page));
            typeof(ConciliacionTarjetas).GetField("_feedback",Flags)!.SetValue(capture.Page,null);
            typeof(ComponentBase).GetMethod("StateHasChanged",Flags)!.Invoke(capture.Page,null);
            Assert.Contains("Iniciá sesión en la base donde querés conciliar",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
        });
    }

    [Fact]
    public async Task MotivoDeAnulacionConservaLoEscritoAlRedibujarLaVentana()
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var sessions=Proxy<ISessionService>((_,_)=>null);var users=Proxy<IAppUserSessionService>((_,_)=>null);
        var service=new ConciliacionTarjetasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);services.AddSingleton(sessions);services.AddSingleton(users);
        services.AddSingleton(runtime.Events);services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton(Proxy<IRouteContextService>((_,_)=>null));services.AddSingleton(Proxy<IJSRuntime>((_,_)=>null));
        var capture=new Captura();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PaginaSinCarga>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            typeof(ConciliacionTarjetas).GetMethod("PedirEliminar",Flags)!.Invoke(capture.Page,[new TarjetasImportacion{Id=1,Nombre="Ensayo.pdf",Cuenta="TJ"}]);
            typeof(ComponentBase).GetMethod("StateHasChanged",Flags)!.Invoke(capture.Page,null);
            var input=capture.Inputs.Single(i=>i.Label=="Motivo de la anulación");
            Assert.Equal("",input.Value);Assert.False(input.Disabled);
            await input.ValueChanged.InvokeAsync("Me equivoqué de archivo");
            Assert.Equal("Me equivoqué de archivo",typeof(ConciliacionTarjetas).GetField("_motivoEliminar",Flags)!.GetValue(capture.Page));
            Assert.Equal("Me equivoqué de archivo",input.Value);
            Assert.DoesNotContain("value=\"_motivoEliminar\"",root.ToHtmlString());
        });
    }

    public sealed class PaginaSinCarga:ConciliacionTarjetas
    {
        protected override void OnInitialized()
        {
            typeof(ConciliacionTarjetas).GetField("_tab",Flags)!.SetValue(this,"conciliar");
            typeof(ConciliacionTarjetas).GetField("_comparacion",Flags)!.SetValue(this,new TarjetasComparacion(new(){Id=1,DetalleCompleto=true},[],[],1));
        }
        protected override Task OnInitializedAsync()=>Task.CompletedTask;
        protected override void OnAfterRender(bool firstRender) { }
    }
    private sealed class Captura:IComponentActivator
    {
        public PaginaSinCarga? Page {get;private set;}
        public List<AlfaInput> Inputs {get;}=[];
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is PaginaSinCarga page)Page=page;
            if(component is AlfaInput input)Inputs.Add(input);return component;
        }
    }
}
