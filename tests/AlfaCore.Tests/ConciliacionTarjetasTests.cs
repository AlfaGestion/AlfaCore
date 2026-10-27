using System.Reflection;
using System.Security.Cryptography;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class ConciliacionTarjetasTests(ITestOutputHelper output)
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }
    private static string Root()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"AlfaCore.sln")))dir=dir.Parent;
        return dir?.FullName??throw new InvalidOperationException("No se encontró el repositorio.");
    }
    private static TarjetasPdfLector Lector()=>new(Proxy<IWebHostEnvironment>((m,_)=>m.Name=="get_ContentRootPath"?Path.Combine(Root(),"src/AlfaCore"):null));
    [Theory]
    [InlineData(100,100,0,"")]
    [InlineData(100,99.78,1,"Diferencia de redondeo revisada")]
    public void DiferenciasAceptadasConservanLosImportes(decimal resumen,decimal alfa,decimal tolerancia,string motivo)
        => ConciliacionTarjetasService.ValidarDiferencia(resumen,alfa,tolerancia,motivo);
    [Fact]
    public void NoSeAceptanDiferenciasSinMotivoNiFueraDeMargen()
    {
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDiferencia(100,99.78m,1,""));
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDiferencia(100,98,1,"Revisado"));
    }
    [Fact]
    public async Task UnArchivoQueNoEsPdfNoIniciaElLector()
        => await Assert.ThrowsAsync<InvalidOperationException>(()=>Lector().LeerAsync(new("falso.pdf",[1,2,3]),"python",default));
    [Fact]
    public void PropuestasNoReutilizanImportesAmbiguosNiMovimientosConciliados()
    {
        var fecha=new DateTime(2025,9,1);
        var datos=new TarjetasComparacion(new(),[new(){Id=1,Fecha=fecha,Importe=100},new(){Id=2,Fecha=fecha,Importe=200},new(){Id=3,Fecha=fecha,Importe=300}],
            [new(){Clave="A",Fecha=fecha,Importe=100},new(){Clave="B",Fecha=fecha,Importe=200},new(){Clave="C",Fecha=fecha,Importe=200},new(){Clave="D",Fecha=fecha,Importe=300,LegacyLiquidado=true}],1);
        Assert.Equal((1L,"A"),Assert.Single(TarjetasPropuestas.Unicas(datos)));
    }

    [TarjetasSqlFact]
    public async Task ImportacionOriginalDuplicadosConfirmacionDesconciliacionYPermisosSinCambiarAsientos()
    {
        var connection=Environment.GetEnvironmentVariable("TARJETAS_TEST_SQL")!;
        Assert.Equal("PRUEBAS02122025",new SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        var before=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS");
        var usuario=await cn.ExecuteScalarAsync<string>("SELECT TOP(1) RTRIM(NOMBRE) FROM dbo.TA_USUARIOS WHERE AdminCentral=1 ORDER BY NOMBRE");
        Assert.False(string.IsNullOrEmpty(usuario));
        var session=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>usuario,_=>null});
        using var runtime=new CargaComprobantesArcaRuntime();
        var service=new ConciliacionTarjetasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,Lector());
        var path=Environment.GetEnvironmentVariable("TARJETAS_TEST_PDF")!;
        var file=new ConciliacionArchivo(Path.GetFileName(path),await File.ReadAllBytesAsync(path));
        var lectura=await service.LeerAsync(file);Assert.True(lectura.DetalleCompleto);
        var cuenta=(await service.CuentasAsync()).Single(x=>x.Descripcion.Contains("Cabal",StringComparison.OrdinalIgnoreCase)).Codigo;
        long? real=null,control=null;
        try
        {
            real=await service.ImportarAsync(file,cuenta,"",false);
            var original=await service.OriginalAsync(real.Value);Assert.Equal(file.Contenido,original.Contenido);
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ImportarAsync(file with{Nombre="renombrado.pdf"},cuenta,"",false));
            var lista=await service.CompararAsync(new(real.Value,new(2025,8,1),new(2025,10,31),"","Todos"));
            Assert.NotEmpty(lista.Movimientos);
            Assert.Equal(lista.Movimientos.Count,lista.Movimientos.Select(x=>x.Clave).Distinct().Count());
            await VerificarPantalla(service,sessions,users,runtime.Events,(await service.ImportacionesAsync()).Single(x=>x.Id==real.Value));
            var movimiento=lista.Movimientos.First(x=>x.Vinculo is null&&!x.LegacyLiquidado);
            var restringida=await service.CompararAsync(new(real.Value,new(2025,8,1),new(2025,10,31),"NO_EXISTE","Todos"));Assert.Empty(restringida.Movimientos);
            // Datos auxiliares controlados para verificar el vínculo sin insertar/modificar contabilidad real.
            var hash=Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray()));
            control=await cn.ExecuteScalarAsync<long>("""
                INSERT dbo.TJ_Importacion(Nombre,Original,Hash,Banco,Comercio,Tarjeta,Cuenta,Sucursal,Desde,Hasta,Bruto,Neto,DetalleCompleto,Avisos,Usuario)
                OUTPUT INSERTED.Id VALUES('PRUEBA_AUTOMATICA.pdf',@original,@hash,'PRUEBA','PRUEBA','PRUEBA',@cuenta,'',@fecha,@fecha,@importe,@importe,1,'','PRUEBA_AUTOMATICA')
                """,new{original=file.Contenido,hash,cuenta,fecha=movimiento.Fecha,importe=movimiento.Importe});
            var detalle=await cn.ExecuteScalarAsync<long>("INSERT dbo.TJ_Detalle(ImportacionId,Fecha,Liquidacion,Lote,Cupon,Terminal,Nivel,Cantidad,Importe,Huella) OUTPUT INSERTED.Id VALUES(@id,@fecha,'PRUEBA','PRUEBA','','','Lote',1,@importe,@hash)",new{id=control.Value,fecha=movimiento.Fecha,importe=movimiento.Importe,hash});
            var filtro=new TarjetasFiltro(control.Value,new(2025,8,1),new(2025,10,31),"","Todos");
            var modelo=Proxy<IConciliacionIaModelo>((_,args)=>{
                var contexto=(ConciliacionIaContexto)args![0]!;
                Assert.True(contexto.EsTarjetas);Assert.Single(contexto.Banco);
                var indice=contexto.Alfa.ToList().FindIndex(x=>x.Huella==movimiento.Huella);
                Assert.True(indice>=0);
                return Task.FromResult(new ConciliacionIaRespuesta("ensayo",System.Text.Json.JsonSerializer.Serialize(new ConciliacionIaSalida{
                    Grupos=[new(){Banco=[0],Alfa=[indice],Confianza="Media",Motivo="Coincidencia de control; revisar"}]})));
            });
            var asistido=new ConciliacionTarjetasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,Lector(),modelo);
            var analisis=await asistido.AnalizarIaAsync(filtro);
            Assert.Equal(movimiento.Clave,Assert.Single(Assert.Single(analisis.Propuestas).Movimientos).Clave);
            Assert.Null((await service.CompararAsync(filtro)).Detalles.Single().Vinculo);
            var solicitud=new TarjetasConfirmar(filtro,[detalle],[new(movimiento.Clave,movimiento.Huella)],"Prueba automática");
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ConfirmarAsync(solicitud with{Movimientos=[new(movimiento.Clave,"HUELLA_OBSOLETA")]}));
            var vinculo=await service.ConfirmarAsync(solicitud);
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ConfirmarAsync(solicitud));
            var conciliado=await service.CompararAsync(filtro);Assert.Equal(vinculo,conciliado.Detalles.Single().Vinculo);
            await cn.ExecuteAsync("UPDATE dbo.TJ_VinculoAlfa SET Clave=@clave WHERE VinculoId=@id",new{clave=movimiento.ClaveAnterior,id=vinculo});
            var anterior=await service.CompararAsync(filtro);
            Assert.Equal(movimiento.Clave,Assert.Single(anterior.Movimientos.Where(x=>x.Vinculo==vinculo)).Clave);
            await Assert.ThrowsAsync<AppUserFacingException>(()=>service.DesconciliarAsync(vinculo,filtro,""));
            await service.DesconciliarAsync(vinculo,filtro,"Reversión de prueba");
            Assert.Null((await service.CompararAsync(filtro)).Detalles.Single().Vinculo);
            Assert.Equal("Reversión de prueba",await cn.ExecuteScalarAsync<string>("SELECT MotivoAnulacion FROM dbo.TJ_Vinculo WHERE Id=@id",new{id=vinculo}));
            var denegado=new ConciliacionTarjetasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(false)),runtime.Events,Lector());
            await Assert.ThrowsAsync<AppUserFacingException>(()=>denegado.OriginalAsync(real.Value));
            Assert.Equal(before,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS"));
            output.WriteLine($"Importación real #{real}, vínculo de control #{vinculo}; original idéntico, duplicados y huellas obsoletas rechazados, reversión auditada y sin asientos nuevos.");
        }
        finally
        {
            foreach(var id in new[]{control,real}.Where(x=>x.HasValue))
                await cn.ExecuteAsync("DELETE a FROM dbo.TJ_VinculoAlfa a JOIN dbo.TJ_Vinculo v ON v.Id=a.VinculoId WHERE v.ImportacionId=@id; DELETE d FROM dbo.TJ_VinculoDetalle d JOIN dbo.TJ_Vinculo v ON v.Id=d.VinculoId WHERE v.ImportacionId=@id; DELETE dbo.TJ_Vinculo WHERE ImportacionId=@id; DELETE dbo.TJ_Detalle WHERE ImportacionId=@id; DELETE dbo.TJ_Importacion WHERE Id=@id",new{id=id!.Value});
        }
    }
    private async Task VerificarPantalla(ConciliacionTarjetasService service,ISessionService sessions,IAppUserSessionService users,IAppEventService events,TarjetasImportacion importacion)
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(service);services.AddSingleton(sessions);services.AddSingleton(users);services.AddSingleton(events);
        services.AddSingleton<IPageHeaderService,PageHeaderService>();services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton<NavigationManager,CargaComprobantesUiTests.TestNavigation>();services.AddSingleton<IRouteContextService,RouteContextService>();
        services.AddSingleton(Proxy<IAppModeService>((m,_)=>m.Name=="get_IsSaaSMode"?true:null));
        services.AddSingleton(Proxy<IJSRuntime>((_,_)=>null));
        var capture=new Capture();services.AddSingleton<IComponentActivator>(capture);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        string sinDialogo="";
        var html=await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<AlfaCore.Components.Pages.ConciliacionTarjetas>();
            var header=provider.GetRequiredService<IPageHeaderService>();Assert.Equal("caja-bancos",header.Current.ModuleKey);
            Assert.True(header.Current.Actions.Single(x=>x.Key=="importar").Disabled);
            var page=capture.Page!;
            await (Task)page.GetType().GetMethod("Abrir",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,[importacion])!;
            var result=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());Assert.Contains("Liquidación",result);Assert.Contains("Alfa · tarjetas presentadas",result);
            Assert.DoesNotContain("value=\"_filtro",result);Assert.Contains("aria-label=\"Seleccionar registro del resumen\"",result);
            Assert.Contains("Seleccionar todo",result);Assert.Contains("Invertir",result);Assert.Contains("Solo seleccionados",result);
            Assert.Contains("Seleccionar página de liquidación",result);Assert.Contains("Diferencia de selección",result);
            Assert.NotNull(header.Current.SearchContent);
            Assert.False(header.Current.Actions.Single(x=>x.Key=="ia").Disabled);
            await header.Current.Actions.Single(x=>x.Key=="proponer").OnClick!();
            var comparacion=(TarjetasComparacion)page.GetType().GetField("_comparacion",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)!;
            sinDialogo=root.ToHtmlString();
            var propuesta=new TarjetasIaPropuesta("Media","Revisar lote y cupón antes de confirmar",[comparacion.Detalles.First()],[comparacion.Movimientos.First()]);
            page.GetType().GetField("_ia",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(page,new TarjetasIaResultado("ensayo",1,1,0,3,[propuesta]));
            page.GetType().GetField("_mostrarIa",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(page,true);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,null);
            Assert.Contains("Revisar esta propuesta",System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            return root.ToHtmlString();
        });
        if(string.IsNullOrEmpty(CargaComprobantesBrowserFactAttribute.BrowserPath))return;
        var css=File.ReadAllText(Path.Combine(Root(),"src/AlfaCore/wwwroot/css/alfacore-design.css"))+File.ReadAllText(Path.Combine(Root(),"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
        using var pw=await Playwright.CreateAsync();await using var browser=await pw.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=CargaComprobantesBrowserFactAttribute.BrowserPath});
        var tab=await browser.NewPageAsync();await tab.RouteAsync("**/*",r=>r.AbortAsync());
        foreach(var width in new[]{2048,1440,1024})
        {
            await tab.SetViewportSizeAsync(width,900);await tab.SetContentAsync("<html><head><style>body{margin:0;padding:12px;box-sizing:border-box;background:var(--alfa-bg-canvas);color:var(--alfa-text-primary);font:13px system-ui,sans-serif}*{box-sizing:border-box}"+css+"</style></head><body>"+sinDialogo+"</body></html>");
            Assert.True(await tab.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 2"));
            Assert.Equal("sticky",await tab.Locator(".tarjetas-tabla th").First.EvaluateAsync<string>("e=>getComputedStyle(e).position"));
            await tab.ScreenshotAsync(new(){Path=Path.Combine(Path.GetTempPath(),$"alfacore-tarjetas-{width}.png")});
            await tab.SetContentAsync("<html><head><style>body{margin:0;padding:12px;box-sizing:border-box;background:var(--alfa-bg-canvas);color:var(--alfa-text-primary);font:13px system-ui,sans-serif}*{box-sizing:border-box}"+css+"</style></head><body>"+html+"</body></html>");
            Assert.True(await tab.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 2"));
            output.WriteLine($"Vista {width}: sin overflow horizontal global y encabezados fijos.");
        }
    }
    private sealed class Capture:IComponentActivator
    {
        public AlfaCore.Components.Pages.ConciliacionTarjetas? Page {get;private set;}
        public IComponent CreateInstance(Type type){var c=(IComponent)Activator.CreateInstance(type)!;if(c is AlfaCore.Components.Pages.ConciliacionTarjetas page)Page=page;return c;}
    }
}
public sealed class TarjetasSqlFactAttribute:FactAttribute
{
    public TarjetasSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_TEST_SQL"))||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_TEST_PDF")))Skip="Requiere base de pruebas y PDF autorizados en TARJETAS_TEST_SQL/PDF.";}
}
