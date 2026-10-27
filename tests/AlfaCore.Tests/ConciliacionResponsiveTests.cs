using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConciliacionResponsiveTests
{
    [CargaComprobantesBrowserFact]
    public async Task FiltrosYAplicarQuedanDentroDeLaVentanaYLasAccionesSeReubican()
    {
        var services=new ServiceCollection();services.AddLogging();
        var header=new PageHeaderService();services.AddSingleton<IPageHeaderService>(header);
        services.AddSingleton<IPageHeaderNavigationService,PageHeaderNavigationService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton(DispatchProxy.Create<IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton<AlfaActionMenuCoordinator>();
        var capture=new Capture();services.AddSingleton<IComponentActivator>(capture);
        header.Set(new() {
            ModuleKey="caja-bancos",ShellMode=PageHeaderShellMode.AlfaDesignPilot,ResponsiveToolbar=true,
            SearchContent=b=> { b.OpenComponent<ConciliacionFiltrosBusqueda>(0);b.AddAttribute(1,"Model",new ConciliacionBusqueda());b.CloseComponent(); },
            Actions=[new(){Key="conciliar",Label="Conciliar selección",Style=PageHeaderActionStyle.Primary},
                new(){Key="propuestas",Label="Revisar propuestas",CollapseOnNarrow=true},
                new(){Key="comparar",Label="Comparar"},new(){Key="ia",Label="Asistir con IA"},
                new(){Key="asiento",Label="Generar asiento",CollapseOnNarrow=true},
                new(){Key="criterios",Label="Criterios",Priority=PageHeaderActionPriority.Overflow}]
        });
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=> {
            var root=await renderer.RenderComponentAsync<CascadingValue<bool>>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Name"]="AlfaDesignShellActive",["Value"]=true,["ChildContent"]=(RenderFragment)(b=>{b.OpenComponent<MainPageHeader>(0);b.CloseComponent();}) }));
            typeof(GestionSmartSearch).GetMethod("Toggle",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.Search,null);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.Search,null);
            Assert.Contains(capture.Menus,m=>m.Items.Any(i=>i.Key=="asiento")&&m.Items.Any(i=>i.Key=="propuestas"));
            return root.ToHtmlString();
        });
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"AlfaCore.sln")))repo=repo.Parent;
        Assert.NotNull(repo);
        var css=File.ReadAllText(Path.Combine(repo!.FullName,"src/AlfaCore/wwwroot/app.css"))+
            File.ReadAllText(Path.Combine(repo.FullName,"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"))+
            File.ReadAllText(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/css/alfacore-design.css"));
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=CargaComprobantesBrowserFactAttribute.BrowserPath});
        var page=await browser.NewPageAsync();
        foreach(var (width,height) in new[]{(2048,768),(1440,768),(1280,768),(1024,768),(800,768),(640,768),(390,768),(1024,600),(800,480)})
        {
            await page.SetViewportSizeAsync(width,height);
            await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style><style>body{{margin:0;font-family:Segoe UI}}*{{box-sizing:border-box}}</style></head><body><div class='shell shell--alfa-design' style='display:flex;flex-direction:column'><div style='height:80px;flex:none'></div>{html}</div></body></html>");
            var panel=page.Locator(".conciliacion-busqueda .smart-search__panel");
            var path=Path.Combine(repo.FullName,$"artifacts/conciliacion/filtros-responsive-{width}-{height}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);await page.ScreenshotAsync(new(){Path=path});
            var bounds=await panel.EvaluateAsync<string>("e=>JSON.stringify({panel:e.getBoundingClientRect().toJSON(),search:e.parentElement.getBoundingClientRect().toJSON()})");
            Assert.True(await panel.EvaluateAsync<bool>("e=>{const r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}"),$"Panel fuera de ventana: {width}: {bounds}");
            var aplicar=panel.GetByRole(AriaRole.Button,new(){Name="Aplicar",Exact=true});
            Assert.True(await aplicar.EvaluateAsync<bool>("e=>{const r=e.getBoundingClientRect(),p=e.closest('.smart-search__panel').getBoundingClientRect();return r.left>=p.left&&r.right<=p.right&&r.top>=p.top&&r.bottom<=p.bottom;}"),$"Aplicar fuera del panel: {width}");
            Assert.True(await aplicar.EvaluateAsync<bool>("e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}"),$"Aplicar no es accesible: {width}x{height}");
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth<=innerWidth"),$"Desborde horizontal: {width}");
            Assert.Equal(width>1440,await page.Locator(".main-page-header__action--collapse").First.IsVisibleAsync());
            Assert.Equal(width<=1440,await page.Locator(".main-page-header__overflow-narrow button").IsVisibleAsync());
        }
    }
    private sealed class Capture:IComponentActivator
    {
        public GestionSmartSearch? Search {get;private set;}
        public List<AlfaActionMenu> Menus {get;}=[];
        public IComponent CreateInstance(Type type) {
            var instance=(IComponent)Activator.CreateInstance(type)!;
            if(instance is GestionSmartSearch search)Search=search;
            if(instance is AlfaActionMenu menu)Menus.Add(menu);
            return instance;
        }
    }
}
