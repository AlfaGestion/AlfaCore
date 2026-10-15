using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using AlfaCore.Components.Pages;
using Microsoft.Playwright;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class ReportesComercialesTests
{
    [Fact]
    public async Task ConfiguracionBuscaDesdeToolbarYFiltraPorArea()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        var routes=DispatchProxy.Create<IRouteContextService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)routes).Handler=(_,a)=>"/ALFANET/153"+(string)a![0]!;
        services.AddSingleton(routes);
        var menus=DispatchProxy.Create<IMenuService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)menus).Handler=(m,_)=>m.Name switch
        { "GetModulesAsync"=>Task.FromResult<IReadOnlyList<ShellModuleDto>>([]),"GetSearchItemsAsync"=>Task.FromResult<IReadOnlyList<ShellMenuSearchItemDto>>([]),_=>null };
        services.AddSingleton(menus);
        var capture=new Capture();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<ConfiguracionHub>();
            var header=provider.GetRequiredService<IPageHeaderService>();Assert.NotNull(header.Current.SearchContent);
            Assert.DoesNotContain("hub-buscar",root.ToHtmlString());
            var hub=capture.Hub!;
            typeof(ConfiguracionHub).GetMethod("OnTextoChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(hub,["MaxDto"]);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(hub,null);
            Assert.Contains("tab=carga-comprobantes",root.ToHtmlString());
            Assert.DoesNotContain("Correo saliente",root.ToHtmlString());
            typeof(ConfiguracionHub).GetMethod("CambiarArea",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(hub,[ConfiguracionCatalogo.Areas.First()]);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(hub,null);
            Assert.Contains("Sin resultados",root.ToHtmlString());
        });
    }
    [Fact]
    public void RechazaPeriodoYPaginacionInvalidosYBuscaConfiguracionPorClave()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => ReporteComercialService.Validar(new() { FechaDesde=new(2026,10,15),FechaHasta=new(2024,1,1) }));
        Assert.ThrowsAny<InvalidOperationException>(() => ReporteComercialService.Validar(new() { Pagina=0 }));
        Assert.ThrowsAny<InvalidOperationException>(() => ReporteComercialService.Validar(new() { TamanioPagina=501 }));
        Assert.Contains(ConfiguracionCatalogo.Buscar(ConfiguracionCatalogo.Entradas,"MaxDto"),x=>x.Clave=="carga-comprobantes");
        Assert.Contains(ConfiguracionCatalogo.Buscar(ConfiguracionCatalogo.Entradas,"homologación"),x=>x.Ruta.Contains("tab=factura"));
    }

    [CargaComprobantesSqlFact]
    public async Task ComprasYVentasConcuerdanConLibroYCuentaCorrienteSoloIncluyeTerceros()
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var session=new SessionDto(); var authorized=true;
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler=(m,_)=>m.Name switch
        { "GetActiveSession"=>session,"GetConnectionString"=>Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"),_=>null };
        var users=DispatchProxy.Create<IAppUserSessionService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler=(m,_)=>m.Name switch
        { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>authorized,"GetCurrentUserName"=>"Administrador",_=>null };
        var permissions=DispatchProxy.Create<IPermissionService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)permissions).Handler=(_,_)=>Task.FromResult(true);
        var service=new ReporteComercialService(runtime.Sessions,users,permissions,runtime.Events);
        var exporter=new ReporteComprasExcelExporter();
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        foreach(var ventas in new[]{false,true})
        {
            var fuente=ventas?"Libro_VentasConFP":"Libro_ComprasConFP";
            var fecha=await cn.ExecuteScalarAsync<DateTime>($"SELECT MAX(FECHA) FROM dbo.{fuente}");
            var f=new FiltrosReporteCompras { FechaDesde=fecha.Date,FechaHasta=fecha.Date,TamanioPagina=500 };
            var opciones=await service.GetOptionsAsync(ventas);Assert.NotEmpty(opciones.TiposComprobante);
            var resumen=await service.GetResumenAsync(ventas,f);
            var esperado=await cn.QuerySingleAsync<(int Cantidad,decimal Importe,decimal Iva)>($"""
                SELECT COUNT(*),SUM(IMPORTE),SUM(ISNULL(LIVA_ImpIVA,0)+ISNULL(LIVA_ImpIVARec,0)+ISNULL(LIVA_ImpIva2,0)+ISNULL(LIVA_ImpIVA3,0)+ISNULL(LIVA_ImpIVA4,0))
                FROM dbo.{fuente} WHERE FECHA>=@Desde AND FECHA<DATEADD(day,1,@Desde)
                """,new { Desde=fecha.Date });
            Assert.Equal(esperado.Cantidad,resumen.CantidadComprobantes);
            Assert.Equal(esperado.Importe,resumen.TotalComprado);Assert.Equal(esperado.Iva,resumen.IvaTotal);
            var detalle=await service.GetDetalleAsync(ventas,f);Assert.Equal(esperado.Cantidad,detalle.TotalRegistros);
            Assert.NotEmpty(detalle.Items);
            f.TamanioPagina=1;
            var primera=await service.GetDetalleAsync(ventas,f);Assert.Single(primera.Items);Assert.Equal(esperado.Cantidad,primera.TotalRegistros);
            f.Proveedor=primera.Items[0].Cuenta;
            var filtrado=await service.GetDetalleAsync(ventas,f);Assert.All(filtrado.Items,x=>Assert.Contains(f.Proveedor,x.Cuenta));
            f.Proveedor=null;f.TamanioPagina=500;
            var resumenBytes=exporter.ExportarResumen(resumen,f,ventas);
            using(var wb=new XLWorkbook(new MemoryStream(resumenBytes)))
                Assert.Equal(ventas?"Resumen de Ventas":"Resumen de Compras",wb.Worksheet(1).Cell(1,1).GetString());
            using(var wb=new XLWorkbook(new MemoryStream(exporter.ExportarDetalle(detalle.Items,f,detalle.TotalRegistros,ventas))))
                Assert.Equal(ventas?"Cliente":"Proveedor",wb.Worksheet(1).Cell(3,5).GetString());
            var cc=await service.GetCuentaCorrienteAsync(ventas,f);Assert.NotEmpty(cc.Items);
            var maestro=ventas?"VT_CLIENTES":"VT_PROVEEDORES";var signo=ventas?"D":"H";
            var saldo=await cn.ExecuteScalarAsync<decimal>($"""
                SELECT ISNULL(SUM(CASE WHEN a.[DEBE-HABER]=@Signo THEN ISNULL(a.IMPORTE,0) ELSE -ISNULL(a.IMPORTE,0) END),0)
                FROM dbo.MV_ASIENTOS a WHERE FECHA>=@Desde AND FECHA<DATEADD(day,1,@Desde)
                AND EXISTS(SELECT 1 FROM dbo.{maestro} p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA)))
                """,new { Desde=fecha.Date,Signo=signo });
            Assert.Equal(saldo,cc.SaldoTotal);
            foreach(var item in cc.Items)
                Assert.True(await cn.ExecuteScalarAsync<bool>($"SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.{maestro} WHERE LTRIM(RTRIM(CTACODIGO))=@Cuenta) THEN 1 ELSE 0 END)",new { item.Cuenta }));
            using(var wb=new XLWorkbook(new MemoryStream(exporter.ExportarCuentaCorriente(cc.Items,f,cc.SaldoTotal,ventas))))
                Assert.Equal(ventas?"Cliente":"Proveedor",wb.Worksheet(1).Cell(3,2).GetString());
            // Filtrar por tipo y fecha vuelve a resolver las mismas sumas, sin cambiar el signo de NC.
            if(ventas)
            {
                f.TipoComprobante="NC";
                var nc=await service.GetDetalleAsync(true,f);Assert.NotEmpty(nc.Items);Assert.All(nc.Items,x=>Assert.True(x.Total<=0 && x.Iva<=0));
            }
            await ProbarPantallaAsync(service,exporter,runtime.Sessions,users,ventas,f);
        }
        authorized=false;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GetResumenAsync(true,new()));
    }

    private static async Task ProbarPantallaAsync(ReporteComercialService service,ReporteComprasExcelExporter exporter,
        ISessionService sessions,IAppUserSessionService users,bool ventas,FiltrosReporteCompras filtro)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);services.AddSingleton(exporter);
        services.AddSingleton(sessions);services.AddSingleton(users);services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();
        var route=DispatchProxy.Create<IRouteContextService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)route).Handler=(_,args)=>"/ALFANET/153"+(string)args![0]!;
        services.AddSingleton(route);
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        var capture=new Capture();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<ReporteComercialPanel>(ParameterView.FromDictionary(new Dictionary<string,object?> { [nameof(ReporteComercialPanel.Ventas)]=ventas }));
            var panel=capture.Panel!;var header=provider.GetRequiredService<IPageHeaderService>();Assert.NotNull(header.Current.SearchContent);
            Assert.Equal(ventas?"ventas":"compras",header.Current.ModuleKey);
            var f=(FiltrosReporteCompras)typeof(ReporteComercialPanel).GetField("_filtros",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel)!;
            f.FechaDesde=filtro.FechaDesde;f.FechaHasta=filtro.FechaHasta;
            await header.Current.Actions.Single(x=>x.Key=="generar").OnClick!();
            Assert.False(header.Current.Actions.Single(x=>x.Key=="excel").Disabled);
            Assert.Contains("Total del período",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            typeof(ReporteComercialPanel).GetMethod("OnTipoReporteChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,["CuentaCorriente"]);
            await header.Current.Actions.Single(x=>x.Key=="generar").OnClick!();
            Assert.Contains("Movimiento neto del período",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            if(CargaComprobantesBrowserFactAttribute.BrowserPath is not null)
            {
                var dir=new DirectoryInfo(AppContext.BaseDirectory);
                while(dir is not null && !File.Exists(Path.Combine(dir.FullName,"src/AlfaCore/AlfaCore.csproj")))dir=dir.Parent;
                var carpeta=Path.Combine(dir!.FullName,"src/AlfaCore/wwwroot");
                var css=File.ReadAllText(Path.Combine(carpeta,"css/alfacore-design.css"))+File.ReadAllText(Path.Combine(carpeta,"app.css"))+File.ReadAllText(Path.Combine(carpeta,"css/gestion-alfadesign.css"));
                using var pw=await Playwright.CreateAsync();await using var browser=await pw.Chromium.LaunchAsync(new() { Headless=true,ExecutablePath=CargaComprobantesBrowserFactAttribute.BrowserPath });
                var page=await browser.NewPageAsync();
                foreach(var width in new[]{2048,1440,1024})
                {
                    await page.SetViewportSizeAsync(width,900);await page.SetContentAsync($"<style>{css}</style>{root.ToHtmlString()}");
                    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
                    Assert.True(await page.GetByText("Movimiento neto del período",new() { Exact=true }).IsVisibleAsync());
                }
            }
            typeof(ReporteComercialPanel).GetMethod("OnTipoReporteChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,["DetalleDeCompras"]);
            await header.Current.Actions.Single(x=>x.Key=="generar").OnClick!();
            Assert.Contains("adm-link-button",root.ToHtmlString());
            provider.GetRequiredService<NavigationManager>().NavigateTo("/ALFANET/153/ventas/reportes?directo=1");
            await header.Current.Actions.Single(x=>x.Key=="generar").OnClick!();
            Assert.DoesNotContain("adm-link-button",root.ToHtmlString());
        });
    }
    private sealed class Capture : IComponentActivator
    {
        public ReporteComercialPanel? Panel { get; private set; }
        public ConfiguracionHub? Hub { get; private set; }
        public IComponent CreateInstance(Type type) { var instance=(IComponent)Activator.CreateInstance(type)!;if(instance is ReporteComercialPanel p)Panel=p;if(instance is ConfiguracionHub h)Hub=h;return instance; }
    }
}
