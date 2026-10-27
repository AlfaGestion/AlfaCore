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
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is PaginaSinCarga page)Page=page;return component;
        }
    }
}
