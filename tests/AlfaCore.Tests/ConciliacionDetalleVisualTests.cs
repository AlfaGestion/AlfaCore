using System.Globalization;
using System.Reflection;
using PaginaConciliacion = AlfaCore.Components.Pages.ConciliacionBancaria;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class ConciliacionDetalleVisualTests
{
    [ConciliacionSqlFact]
    public async Task DetallePriorizaSaldoAlCorteConservaMonedasYSeparaFormulariosEnOtraSolapa()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog);
        T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
        {
            var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
        }
        var session=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch { "GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null });
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null });
        var permissions=Proxy<IPermissionService>((_,_)=>Task.FromResult(true));
        var service=new ConciliacionBancariaService(sessions,users,permissions,runtime.Events,new());
        var row=(await service.MovimientosAsync(new(){Buscar="548"})).Items.Single(m=>m.Id==548);
        row.Estado="Pendiente"; // Solo el DTO de la pantalla: no se modifica el movimiento real.
        var esperado=await service.CuentaCorrienteAsync(row.Id,30,row.Fecha.AddDays(15));
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(service);services.AddSingleton(sessions);services.AddSingleton(users);services.AddSingleton(runtime.Events);
        services.AddSingleton(new CobranzasService(sessions,users,permissions,runtime.Events));
        services.AddSingleton<IPageHeaderService,PageHeaderService>();services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();services.AddSingleton<IRouteContextService,RouteContextService>();
        services.AddSingleton(Proxy<IAppModeService>((m,_)=>m.Name=="get_IsSaaSMode" ? true : null));
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));
        var activator=new CapturingActivator();services.AddSingleton<IComponentActivator>(activator);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        string html="",reconocimientoHtml="",sinReciboHtml="";
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<PaginaConciliacion>();
            var page=activator.Page!;
            typeof(PaginaConciliacion).GetField("_tab",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(page,"revisar");
            await (Task)typeof(PaginaConciliacion).GetMethod("SeleccionarMovimiento",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,[row])!;
            await root.QuiescenceTask;
            var campoSaldo=typeof(PaginaConciliacion).GetField("_saldoDetalle",BindingFlags.Instance|BindingFlags.NonPublic)!;
            for(var intento=0;campoSaldo.GetValue(page) is null && intento<250;intento++) await Task.Delay(20);
            var panel=activator.Panel!;
            var ocupado=panel.GetType().GetField("_busy",BindingFlags.Instance|BindingFlags.NonPublic)!;
            for(var intento=0;(bool)ocupado.GetValue(panel)! && intento<250;intento++) await Task.Delay(20);
            Assert.NotNull(panel.GetType().GetField("_asiento",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel));
            var saldo=(ConciliacionSaldoResumen?)campoSaldo.GetValue(page);
            Assert.NotNull(saldo);Assert.Equal(row.Fecha.Date.AddDays(15),saldo.FechaCorte);
            Assert.Equal(esperado.Saldos.Select(s=>(s.Moneda,s.Saldo)),saldo.Saldos.Select(s=>(s.Moneda,s.Saldo)));
            html=root.ToHtmlString();var texto=System.Net.WebUtility.HtmlDecode(html);
            Assert.True(texto.IndexOf("Saldo del cliente",StringComparison.Ordinal)<texto.IndexOf("Clasificación y cliente",StringComparison.Ordinal));
            Assert.Contains("según unidades autorizadas",texto);
            foreach(var s in esperado.Saldos) Assert.Contains(s.Saldo.ToString("N2",CultureInfo.GetCultureInfo("es-AR")),texto);
            Assert.Contains("conciliacion-detalle-cliente",html);Assert.Contains("Movimiento bancario",texto);
            // Una consulta tardía de otro movimiento no debe reemplazar el saldo visible.
            typeof(PaginaConciliacion).GetMethod("ActualizarSaldoDetalle",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(page,[new ConciliacionSaldoResumen(-1,DateTime.Today,[])]);
            Assert.Same(saldo,campoSaldo.GetValue(page));
            await (Task)typeof(PaginaConciliacion).GetMethod("MostrarReconocimientoCobro",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,null)!;
            reconocimientoHtml=root.ToHtmlString();
            Assert.Contains("Confirmá el recibo existente",System.Net.WebUtility.HtmlDecode(reconocimientoHtml));
            Assert.False((bool)panel.GetType().GetField("_confirmoRecibo",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel)!);
            panel.GetType().GetField("_asiento",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(panel,null);
            await panel.IniciarReconocimientoAsync();
            sinReciboHtml=root.ToHtmlString();
            Assert.Contains("Falta seleccionar un recibo válido",System.Net.WebUtility.HtmlDecode(sinReciboHtml));
            Assert.Contains("El saldo cero no demuestra",System.Net.WebUtility.HtmlDecode(sinReciboHtml));
        });
        if(CargaComprobantesBrowserFactAttribute.BrowserPath is null) return;
        var repo=FindRoot();
        var css=await File.ReadAllTextAsync(Path.Combine(repo,"src/AlfaCore/wwwroot/css/alfacore-design.css"))+
            await File.ReadAllTextAsync(Path.Combine(repo,"src/AlfaCore/wwwroot/app.css"))+
            await File.ReadAllTextAsync(Path.Combine(repo,"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=CargaComprobantesBrowserFactAttribute.BrowserPath});
        var browserPage=await browser.NewPageAsync();
        foreach(var width in new[]{2048,1440,1024})
        {
            await browserPage.SetViewportSizeAsync(width,1000);
            await browserPage.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style><style>body{{margin:0;font-family:Segoe UI,sans-serif}}*{{box-sizing:border-box}}</style></head><body>{html}</body></html>");
            var dialog=browserPage.GetByRole(AriaRole.Dialog);
            Assert.True(await dialog.IsVisibleAsync());
            Assert.True(await dialog.Locator(".alfa-dialog__footer").GetByRole(AriaRole.Button,new(){Name="Marcar como cobrado",Exact=true}).IsVisibleAsync());
            Assert.True(await browserPage.GetByLabel("Cliente y saldo",new(){Exact=true}).IsVisibleAsync());
            Assert.False(await browserPage.GetByPlaceholder("Buscar por nombre, código o CUIT").IsVisibleAsync());
            Assert.True(await browserPage.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            Assert.True(await dialog.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth"));
            Assert.True(await browserPage.GetByLabel("Cliente y saldo",new(){Exact=true}).EvaluateAsync<bool>("e => {const a=e.getBoundingClientRect(),b=e.closest('.alfa-dialog__body').getBoundingClientRect();return a.top>=b.top && a.bottom<=b.bottom;}"));
            var receipt=browserPage.GetByLabel("Asiento seleccionado",new(){Exact=true});
            Assert.False(await receipt.EvaluateAsync<bool>("e => e.open"));
            await receipt.Locator("summary").FocusAsync();await browserPage.Keyboard.PressAsync("Enter");
            Assert.True(await receipt.EvaluateAsync<bool>("e => e.open"));
            await browserPage.Keyboard.PressAsync("Enter");
            await browserPage.Locator(".alfa-dialog__body").EvaluateAsync("e => e.scrollTop=0");
            var path=Path.Combine(repo,"artifacts/conciliacion/detalle-saldo-"+width+".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);await browserPage.ScreenshotAsync(new(){Path=path});
        }
        await browserPage.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style></head><body>{reconocimientoHtml}</body></html>");
        Assert.True(await browserPage.GetByLabel("Asiento seleccionado",new(){Exact=true}).EvaluateAsync<bool>("e => e.open"));
        Assert.True(await browserPage.GetByText("Confirmo que este recibo corresponde al movimiento bancario",new(){Exact=true}).IsVisibleAsync());
        await browserPage.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style></head><body>{sinReciboHtml}</body></html>");
        Assert.True(await browserPage.GetByText("Falta seleccionar un recibo válido",new(){Exact=true}).IsVisibleAsync());
        Assert.True(await browserPage.Locator(".alfa-dialog__footer").GetByRole(AriaRole.Button,new(){Name="Marcar como cobrado",Exact=true}).IsVisibleAsync());
    }

    private sealed class CapturingActivator:IComponentActivator
    {
        public PaginaConciliacion? Page {get;private set;}
        public AlfaCore.Components.Shared.ConciliacionCuentaCorrientePanel? Panel {get;private set;}
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is PaginaConciliacion page) Page=page;
            if(component is AlfaCore.Components.Shared.ConciliacionCuentaCorrientePanel panel) Panel=panel;
            return component;
        }
    }
    private static string FindRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir is not null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"src/AlfaCore/AlfaCore.csproj")))return dir.FullName;
        throw new InvalidOperationException("No se encontró la raíz del proyecto.");
    }
}
