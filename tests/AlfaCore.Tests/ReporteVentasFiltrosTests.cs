using System.Reflection;
using AlfaCore.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Xunit;

namespace AlfaCore.Tests;

public sealed partial class CuentaCorrienteVentasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcelInformaCodigoProvinciaYProformasDelFiltroAplicado(bool incluir)
    {
        var f=new FiltrosReporteCompras { CuentaExacta="112010681",Provincia="1",IncluirProforma=incluir,
            Proveedor="hotel",Vendedor="2",Zona="Norte",UnidadesNegocio=["1",""] };
        using var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarDetalle([],f,0,true)));
        var texto=wb.Worksheet(1).Cell(2,1).GetString();
        Assert.Contains("Código exacto: 112010681",texto);
        Assert.Contains("Provincia del cliente: 1",texto);
        Assert.Contains("hotel",texto);Assert.Contains("Sin unidad",texto);
        Assert.Contains(incluir?"Incluye proformas (FP, NCFP, CBFP)":"Sin proformas (excluye FP, NCFP, CBFP)",texto);
        Assert.DoesNotContain("proformas",ReporteComprasExcelExporter.DescribirFiltros(f,false));
    }

    [Fact]
    public async Task BuscadorSeleccionaCodigoPorNombreYProformaSeActivaAlCargarYLimpiar()
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var users=DispatchProxy.Create<IAppUserSessionService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler=(m,_)=>m.Name=="get_IsAuthenticated"?false:null;
        var permisos=DispatchProxy.Create<IPermissionService,CargaComprobantesUiTests.EventOnlyProxy>();
        var service=new ReporteComercialService(runtime.Sessions,users,permisos,runtime.Events);
        var routes=DispatchProxy.Create<IRouteContextService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)routes).Handler=(_,a)=>(string)a![0]!;
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);
        services.AddSingleton(new ReporteComprasExcelExporter());services.AddSingleton(runtime.Sessions);services.AddSingleton(users);
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();services.AddSingleton<IPageHeaderService,PageHeaderService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();services.AddSingleton(routes);
        services.AddSingleton(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        var capture=new FiltroCapture();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            await renderer.RenderComponentAsync<ReporteComercialPanel>(ParameterView.FromDictionary(new Dictionary<string,object?> { [nameof(ReporteComercialPanel.Ventas)]=true }));
            var panel=capture.Panel!;var type=typeof(ReporteComercialPanel);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            type.GetMethod("SeleccionarCliente",flags)!.Invoke(panel,[new ReporteFiltroOpcion("112010681","Hotel Prueba")]);
            type.GetMethod("TogglePanel",flags)!.Invoke(panel,null);
            async Task<string> Html()
            {
                var root=await renderer.RenderComponentAsync<CargaComprobantesUiTests.FragmentHost>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                    [nameof(CargaComprobantesUiTests.FragmentHost.Content)]=provider.GetRequiredService<IPageHeaderService>().Current.SearchContent }));
                return System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            }
            var html=await Html();
            Assert.Contains("Código exacto del cliente",html);Assert.Contains("value=\"112010681 · Hotel Prueba\"",html);
            Assert.Contains("Buscar por código o nombre",html);
            Assert.Contains("Provincia del cliente",html);Assert.DoesNotContain("Incluir proforma",html);
            type.GetMethod("AplicarOpciones",flags)!.Invoke(panel,[new ReporteComprasOptionsDto { PuedeIncluirProforma=true }]);
            type.GetMethod("UpdatePageHeader",flags)!.Invoke(panel,null);
            var habilitado=await Html();
            Assert.Contains("Incluir proforma",habilitado);
            var filtros=(FiltrosReporteCompras)type.GetField("_filtros",flags)!.GetValue(panel)!;
            Assert.True(filtros.IncluirProforma);Assert.Equal("112010681",filtros.CuentaExacta);
            type.GetMethod("CambiarProforma",flags)!.Invoke(panel,[false]);Assert.False(filtros.IncluirProforma);
            await (Task)type.GetMethod("LimpiarAsync",flags)!.Invoke(panel,null)!;
            Assert.True(filtros.IncluirProforma);Assert.Null(filtros.CuentaExacta);
            type.GetMethod("AplicarOpciones",flags)!.Invoke(panel,[new ReporteComprasOptionsDto { PuedeIncluirProforma=false }]);
            Assert.False(filtros.IncluirProforma);
            type.GetMethod("CambiarBusquedaCliente",flags)!.Invoke(panel,["hotel"]);
            await (Task)type.GetMethod("GenerarAsync",flags)!.Invoke(panel,null)!;
            var error=(AppUiMessage)type.GetField("_error",flags)!.GetValue(panel)!;
            Assert.Equal("Seleccioná un cliente",error.Title);
        });
    }

    private sealed class FiltroCapture : IComponentActivator
    {
        public ReporteComercialPanel? Panel { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is ReporteComercialPanel panel)Panel=panel;
            return component;
        }
    }

    [CargaComprobantesSqlFact]
    public async Task UsuarioSinVerProformaNoPuedeForzarLaOpcionDesdeElServicio()
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var service=CrearServicio(runtime,"__REPORTE_USUARIO_NO_EXISTENTE__");
        Assert.False((await service.GetOptionsAsync(true)).PuedeIncluirProforma);
        var f=new FiltrosReporteCompras { IncluirProforma=true };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GetResumenAsync(true,f));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GetDetalleAsync(true,f));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GetCuentaCorrienteAsync(true,f));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GetSaldosVentasAsync(f,true));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GetComprobantesCuentaCorrienteAsync(f,true));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.ValidarFiltrosAsync(true,f));
    }

    [CargaComprobantesSqlFact]
    public async Task CodigoExactoProvinciaYProformasSeAplicanEnTodasLasConsultas()
    {
        using var runtime=new CargaComprobantesArcaRuntime();var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var cliente=await cn.QuerySingleAsync<(string Cuenta,string Provincia)>("""
            SELECT TOP 1 LTRIM(RTRIM(l.CUENTA)),LTRIM(RTRIM(p.PROVINCIA))
            FROM dbo.Libro_VentasConFP l JOIN dbo.VT_CLIENTES p
                ON LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(l.CUENTA))
            WHERE UPPER(LTRIM(RTRIM(l.TC))) NOT IN ('FP','NCFP','CBFP')
                AND NULLIF(LTRIM(RTRIM(p.PROVINCIA)),'') IS NOT NULL ORDER BY l.FECHA DESC;
            """);
        var sugerencias=await service.BuscarClientesAsync(cliente.Cuenta);
        Assert.Contains(sugerencias,x=>x.Codigo==cliente.Cuenta);
        Assert.True(sugerencias.Count<=25);
        var porNombre=await service.BuscarClientesAsync(sugerencias.First(x=>x.Codigo==cliente.Cuenta).Nombre);
        Assert.NotEmpty(porNombre);
        var f=new FiltrosReporteCompras { FechaDesde=null,FechaHasta=null,CuentaExacta="  "+cliente.Cuenta+"  ",
            Provincia=" "+cliente.Provincia+" ",IncluirSaldosCero=true,TamanioPagina=500 };
        var detalle=await service.GetDetalleAsync(true,f);Assert.NotEmpty(detalle.Items);
        Assert.All(detalle.Items,x=> { Assert.Equal(cliente.Cuenta,x.Cuenta);Assert.False(ReporteComercialService.EsProforma(x.Tc)); });
        var cantidad=await cn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Libro_VentasConFP WHERE LTRIM(RTRIM(CUENTA))=@Cuenta
                AND UPPER(LTRIM(RTRIM(TC))) NOT IN ('FP','NCFP','CBFP');
            """,new { cliente.Cuenta });
        Assert.Equal(cantidad,detalle.TotalRegistros);
        Assert.Equal(cantidad,(await service.GetResumenAsync(true,f)).CantidadComprobantes);
        Assert.All((await service.GetCuentaCorrienteAsync(true,f)).Items,x=>Assert.Equal(cliente.Cuenta,x.Cuenta));
        Assert.Equal(cliente.Cuenta,Assert.Single((await service.GetSaldosVentasAsync(f,true)).Items).Cuenta);
        Assert.All((await service.GetComprobantesCuentaCorrienteAsync(f,true)).Items,x=>
        {
            Assert.Equal(cliente.Cuenta,x.Cuenta);Assert.False(ReporteComercialService.EsProforma(x.Tc));
            Assert.All(x.Aplicaciones,a=>Assert.False(ReporteComercialService.EsProforma(a.Tc)));
        });
        var opciones=await service.GetOptionsAsync(true);
        f.IncluirProforma=true;
        if(opciones.PuedeIncluirProforma)
        {
            var todos=await service.GetDetalleAsync(true,f);
            Assert.Equal(await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Libro_VentasConFP WHERE LTRIM(RTRIM(CUENTA))=@Cuenta",new { cliente.Cuenta }),todos.TotalRegistros);
        }
        else
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.GetDetalleAsync(true,f));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.ValidarFiltrosAsync(true,f));
        }
        f.IncluirProforma=false;f.Provincia="__SIN_PROVINCIA__";
        Assert.Empty((await service.GetResumenAsync(true,f)).Filas);
        Assert.Empty((await service.GetDetalleAsync(true,f)).Items);
        Assert.Empty((await service.GetCuentaCorrienteAsync(true,f)).Items);
        Assert.Empty((await service.GetSaldosVentasAsync(f,false)).Items);
        Assert.Empty((await service.GetComprobantesCuentaCorrienteAsync(f,true)).Items);
        f.Provincia=null;f.CuentaExacta=cliente.Cuenta+"%";
        Assert.Empty((await service.GetDetalleAsync(true,f)).Items);
    }
}
