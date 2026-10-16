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

public sealed class ActualizacionesErroresTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AliasViejoSoloSeOmiteSiElNuevoTieneElMismoSql(bool mismoSql)
    {
        using var folder=new ScriptsFolder();
        folder.Add("2026-10-15-001__conversaciones.sql","SELECT 2;");
        folder.Add("2026-10-15-001__ventas_carga_directa_operaciones.sql","SELECT 1;\r\n");
        folder.Add("2026-10-15-004__ventas_carga_directa_operaciones.sql",mismoSql?"SELECT 1;\n":"SELECT 3;");
        if(!mismoSql) { var ex=Assert.Throws<TargetInvocationException>(()=>GetScripts(folder.Path));Assert.Contains("2026-10-15-001",ex.InnerException!.Message);return; }
        var scripts=GetScripts(folder.Path);
        Assert.Equal(2,scripts.Count);
        Assert.Contains(scripts,x=>x.VersionKey=="2026-10-15-004");
        Assert.DoesNotContain(scripts,x=>x.Archivo=="2026-10-15-001__ventas_carga_directa_operaciones.sql");
    }

    [Fact]
    public async Task DuplicadoRealInformaArchivosYUsaLoggingCentral()
    {
        using var folder=new ScriptsFolder();
        folder.Add("2026-10-15-001__uno.sql","SELECT 1;");folder.Add("2026-10-15-001__dos.sql","SELECT 2;");
        var duplicado=Assert.Throws<TargetInvocationException>(()=>GetScripts(folder.Path)).InnerException!;
        var logs=0;
        var events=DispatchProxy.Create<IAppEventService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)events).Handler=(m,_)=> { Assert.Equal("LogErrorAsync",m.Name);logs++;return Task.FromResult("INC-TEST"); };
        var service=new ActualizacionesService(null!,null!,null!,events,null!,null!);
        var method=typeof(ActualizacionesService).GetMethod("ExecuteLoggedAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.MakeGenericMethod(typeof(bool));
        Func<CancellationToken,Task<bool>> consulta=_=>Task.FromException<bool>(duplicado);
        var tarea=(Task<bool>)method.Invoke(service,["Actualizaciones","GetDashboard",consulta,"No se pudo cargar el panel.",CancellationToken.None])!;
        var ex=await Assert.ThrowsAsync<AppUserFacingException>(()=>tarea);
        Assert.Equal(1,logs);Assert.Equal("INC-TEST",ex.ErrorCode);
        Assert.Contains("uno.sql",ex.UserMessage);Assert.Contains("dos.sql",ex.UserMessage);
    }

    [Fact]
    public async Task FalloDeCargaMuestraDetallePermiteReintentarYNoRecreaAvisoVacio()
    {
        var fallo=true;
        var service=DispatchProxy.Create<IActualizacionesService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)service).Handler=(m,_)=>m.Name=="GetDashboardAsync"
            ? fallo ? Task.FromException<ActualizacionesDashboardDto>(new AppUserFacingException("Hay scripts duplicados: 2026-10-15-001.","INC-TEST"))
                :Task.FromResult(new ActualizacionesDashboardDto { BaseActiva="Base de prueba" }) :null;
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<IAppUserSessionService>(DispatchProxy.Create<IAppUserSessionService,CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        var routes=DispatchProxy.Create<IRouteContextService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)routes).Handler=(_,a)=>"/ALFANET/106"+a![0];services.AddSingleton(routes);
        var capture=new Capture();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<Actualizaciones>();var page=capture.Page!;
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("2026-10-15-001",html);Assert.Contains("INC-TEST",html);Assert.Contains("Reintentar",html);
            Assert.DoesNotContain("Cargando panel de actualizaciones...",html);
            typeof(Actualizaciones).GetField("_message",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(page,"");
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,null);
            Assert.DoesNotContain("No se pudo completar la actualización",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.Contains("2026-10-15-001",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            fallo=false;await provider.GetRequiredService<IPageHeaderService>().Current.Actions.Single(x=>x.Key=="recargar").OnClick!();
            Assert.Contains("Base de prueba",root.ToHtmlString());Assert.DoesNotContain("Reintentar",root.ToHtmlString());
            fallo=true;await provider.GetRequiredService<IPageHeaderService>().Current.Actions.Single(x=>x.Key=="recargar").OnClick!();
            Assert.True(provider.GetRequiredService<IPageHeaderService>().Current.Actions.Single(x=>x.Key=="ejecutar").Disabled);
            Assert.Contains("Reintentar",root.ToHtmlString());
        });
    }

    private static IReadOnlyList<ActualizacionScriptDto> GetScripts(string path)
    {
        var type=typeof(ActualizacionesService).GetNestedType("UpdateSource",BindingFlags.NonPublic)!;
        var source=Activator.CreateInstance(type)!;type.GetProperty("RutaOrigenActiva")!.SetValue(source,path);
        return (IReadOnlyList<ActualizacionScriptDto>)typeof(ActualizacionesService).GetMethod("GetScripts",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[source,true])!;
    }
    private sealed class Capture:IComponentActivator
    {
        public Actualizaciones? Page { get; private set; }
        public IComponent CreateInstance(Type type) { var instance=(IComponent)Activator.CreateInstance(type)!;if(instance is Actualizaciones page)Page=page;return instance; }
    }
    private sealed class ScriptsFolder:IDisposable
    {
        public string Path { get; }=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"AlfaCoreUpdates-"+Guid.NewGuid().ToString("N"));
        public ScriptsFolder()=>Directory.CreateDirectory(Path);
        public void Add(string name,string sql)=>File.WriteAllText(System.IO.Path.Combine(Path,name),sql);
        public void Dispose() { foreach(var file in Directory.GetFiles(Path))File.Delete(file);Directory.Delete(Path); }
    }
}
