using System.Reflection;
using System.Text.Json;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class CobranzaMultipleTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }

    [Fact]
    public void ValidaAplicacionesSinDuplicadosNiDiferenciasYConservaElContratoAnterior()
    {
        var solicitud=new CobranzaSolicitud(Guid.NewGuid(),1,new(2026,10,19),0,[new("BANCO",100)]);
        Assert.Equal(100,CobranzasService.ValidarImportes(solicitud));
        Assert.DoesNotContain("Aplicaciones",JsonSerializer.Serialize(solicitud));
        Assert.Equal(100,CobranzasService.ValidarImportes(solicitud with{Aplicaciones=[new(1,40),new(2,60)]}));
        foreach(var apps in new CobranzaAplicacion[][] { [],[new(1,100),new(1,1)],[new(1,40),new(2,59)],
            [new(1,-1),new(2,101)],[new(1,40.001m),new(2,59.999m)],[new(2,100)] })
            Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(solicitud with{Aplicaciones=apps}));
    }

    [Fact]
    public async Task VentanaPrecargaDosFacturasYDistribuyeElBancoSinGenerarRetencionesNiGrabarAutomaticamente()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));
        var activator=new ActivatorDialog();services.AddSingleton<IComponentActivator>(activator);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        string html="";
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            CobranzaSolicitud? enviada=null;
            var prep=new CobranzaPreparacion {MovimientoId=24,Fecha=new(2026,9,26),CuentaBanco="111",ImporteBanco=857903.92m,
                Documentos=[new(){Id=1,Cliente="123",Nombre="EMPRESA WAM",Unidad="2",Tc="FC",Comprobante="000200003794A",Moneda="1",Saldo=424704.91m},
                    new(){Id=2,Cliente="123",Nombre="EMPRESA WAM",Unidad="2",Tc="FC",Comprobante="000200003814A",Moneda="1",Saldo=433199.01m}],
                AplicacionesSugeridas=[new(1,424704.91m),new(2,433199.01m)],
                Medios=[new(){Cuenta="111",Descripcion="BANCO ENSAYO",Tipo="DB",Moneda="1"}],
                Talonarios=[new(){Talonario=0,Unidad="2",Sucursal=1}]};
            var root=await renderer.RenderComponentAsync<CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Preparacion"]=prep,["OnConfirm"]=EventCallback.Factory.Create<CobranzaSolicitud>(new object(),(CobranzaSolicitud s)=>enviada=s)}));
            Assert.Null(enviada);html=root.ToHtmlString();
            Assert.Contains("Selección precargada",System.Net.WebUtility.HtmlDecode(html));
            var dialog=activator.Dialog!;
            await (Task)typeof(CobranzaRapidaDialog).GetMethod("Confirmar",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null)!;
            Assert.NotNull(enviada);Assert.Equal(2,enviada.Aplicaciones!.Count);
            Assert.Equal(857903.92m,enviada.Aplicaciones.Sum(a=>a.Importe));Assert.Single(enviada.Pagos);
            Assert.Equal(424704.91m,enviada.Aplicaciones.Single(a=>a.FacturaId==1).Importe);
            // Conservar la distribución propuesta aunque la factura tenga ahora más saldo.
            prep.Documentos[0].Saldo=500000m;
            await (Task)typeof(CobranzaRapidaDialog).GetMethod("Confirmar",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null)!;
            Assert.Equal(424704.91m,enviada!.Aplicaciones!.Single(a=>a.FacturaId==1).Importe);
            prep.Documentos[0].Saldo=424704.91m;
            enviada=null;
            typeof(CobranzaRapidaDialog).GetMethod("CambiarSeleccion",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,[prep.Documentos[1],false]);
            await (Task)typeof(CobranzaRapidaDialog).GetMethod("Confirmar",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null)!;
            Assert.Null(enviada); // no se permite perder el resto del depósito al quitar la segunda factura
        });
        if(CargaComprobantesBrowserFactAttribute.BrowserPath is null)return;
        var repo=Root();
        var css=await File.ReadAllTextAsync(Path.Combine(repo,"src/AlfaCore/wwwroot/css/alfacore-design.css"))+
            await File.ReadAllTextAsync(Path.Combine(repo,"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=CargaComprobantesBrowserFactAttribute.BrowserPath});
        var page=await browser.NewPageAsync();
        foreach(var width in new[]{2048,1440,1024})
        {
            await page.SetViewportSizeAsync(width,1000);
            await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style><style>body{{margin:0;font-family:Segoe UI,sans-serif}}*{{box-sizing:border-box}}</style></head><body>{html}</body></html>");
            Assert.Equal(2,await page.GetByRole(AriaRole.Checkbox).CountAsync());
            Assert.Equal(2,await page.GetByRole(AriaRole.Checkbox,new(){Checked=true}).CountAsync());
            Assert.True(await page.GetByRole(AriaRole.Button,new(){Name="Confirmar cobranza CB",Exact=true}).IsEnabledAsync());
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth<=window.innerWidth"));
            Assert.True(await page.GetByRole(AriaRole.Dialog).EvaluateAsync<bool>("e=>e.scrollWidth<=e.clientWidth"));
            var path=Path.Combine(repo,"artifacts/conciliacion/cobranza-multiple-"+width+".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);await page.ScreenshotAsync(new(){Path=path});
        }
    }

    [ConciliacionSqlFact]
    public async Task UnReciboCancelaDosFacturasConRetencionEsIdempotenteYRevierteTodo()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var session=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null});
        var service=new CobranzasService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            var docs=(await cn.QueryAsync<CobranzaDocumento>("""
                WITH Pendientes AS (
                    SELECT f.ID Id,LTRIM(RTRIM(f.CUENTA)) Cliente,LTRIM(RTRIM(f.UNEGOCIO)) Unidad,
                        LTRIM(RTRIM(f.TC)) Tc,f.IDCOMPROBANTE Comprobante,s.SALDO Saldo,
                        COUNT(*) OVER(PARTITION BY f.CUENTA,f.UNEGOCIO) Cantidad
                    FROM dbo.VE_CPTES_SALDOS_VENTAS s JOIN dbo.V_MV_CPTE f
                        ON f.CUENTA=s.CUENTA AND f.TC=s.TC AND f.SUCURSAL=s.SUCURSAL AND f.NUMERO=s.NUMERO AND f.LETRA=s.LETRA
                    WHERE f.IDCOMPLEMENTO=0 AND s.SALDO>100 AND ISNULL(f.ANULADA,0)=0
                        AND LTRIM(RTRIM(f.TC))='FC' AND LTRIM(RTRIM(f.MONEDA))='1'
                        AND EXISTS(SELECT 1 FROM dbo.MA_CUENTASADIC cl WHERE cl.CODIGO=f.CUENTA)
                ) SELECT TOP(2) * FROM Pendientes WHERE Cantidad>=2 ORDER BY Cliente,Unidad,Id;
                """,transaction:tx)).AsList();
            Assert.Equal(2,docs.Count);Assert.Equal(docs[0].Cliente,docs[1].Cliente);Assert.Equal(docs[0].Unidad,docs[1].Unidad);
            var banco=await cn.QueryFirstAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='DB' AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 AND LTRIM(RTRIM(MONEDA))='1';",transaction:tx);
            var ret=await cn.QueryFirstAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='RG' AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 AND LTRIM(RTRIM(MONEDA))='1';",transaction:tx);
            var tal=await cn.QueryFirstAsync<int>("SELECT Talonario FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CB' AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('','0',@Unidad) ORDER BY CASE WHEN LTRIM(RTRIM(UNEGOCIO))=@Unidad THEN 0 ELSE 1 END,Talonario;",docs[0],tx);
            var mov=await cn.ExecuteScalarAsync<long>("""
                INSERT BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco)
                VALUES('Provincia','ENSAYO_MULTIPLE','',SYSUTCDATETIME(),'ENSAYO',1,18,'IMPORTED',@banco);
                DECLARE @i bigint=SCOPE_IDENTITY();
                INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe,MonedaCodigo)
                VALUES(@i,'Provincia','20261019','ENSAYO_MULTIPLE',18,'ARS');
                DECLARE @m bigint=SCOPE_IDENTITY();
                INSERT BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza) VALUES(@m,'Cobranza',99);
                INSERT BK_MovimientoMatch(MovimientoId,TipoMatch,ClienteCodigo,Confianza,ResumenPropuesta) VALUES(@m,'Multiple',@cliente,95,'ENSAYO');
                SELECT @m;
                """,new{banco,cliente=docs[0].Cliente},tx);
            var solicitud=new CobranzaSolicitud(Guid.NewGuid(),docs[0].Id,new(2026,10,19),tal,[new(banco,18),new(ret,2,"RET-ENSAYO")],mov,
                "Cobro dos facturas",[new(docs[0].Id,12),new(docs[1].Id,8)]);
            var resultado=await service.GrabarEnTransaccionAsync(cn,tx,solicitud,default);
            Assert.Equal(20,resultado.Importe);
            // SQL '=' y LEN no detectan relleno final: comparar bytes en las tres tablas.
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM (
                    SELECT TC,CUENTA,CAST(NULL AS nvarchar(4)) TC_PRINT FROM V_MV_CPTE WHERE ID=@CobranzaId
                    UNION ALL SELECT TC,CUENTA,CAST(NULL AS nvarchar(4)) FROM MV_ASIENTOS
                        WHERE TC='CB' AND SUCURSAL=SUBSTRING(@Comprobante,1,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='X'
                    UNION ALL SELECT TC,CUENTA,TC_PRINT FROM MV_APLICACION WHERE TC='CB' AND IDCOMPROBANTE=@Comprobante
                ) x WHERE DATALENGTH(TC)<>DATALENGTH(LTRIM(RTRIM(TC)))
                    OR DATALENGTH(CUENTA)<>DATALENGTH(LTRIM(RTRIM(CUENTA)))
                    OR DATALENGTH(TC_PRINT)<>DATALENGTH(LTRIM(RTRIM(TC_PRINT)));
                """,resultado,tx));
            var apps=(await cn.QueryAsync<(string Origen,decimal Importe,string PrintTc,string PrintId)>("SELECT IDCOMPROBANTE_ORIGEN,IMPORTE,TC_PRINT,IDCOMPROBANTE_PRINT FROM MV_APLICACION WHERE LTRIM(RTRIM(TC))='CB' AND IDCOMPROBANTE=@Comprobante;",resultado,tx)).AsList();
            Assert.Equal(2,apps.Count);Assert.Equal(20,apps.Sum(a=>a.Importe));
            Assert.All(apps,a=>{Assert.Equal("CB",a.PrintTc.Trim());Assert.Equal(resultado.Comprobante,a.PrintId);});
            Assert.Equal(12,apps.Single(a=>a.Origen==docs[0].Comprobante).Importe);Assert.Equal(8,apps.Single(a=>a.Origen==docs[1].Comprobante).Importe);
            foreach(var d in docs)
            {
                var saldo=await cn.ExecuteScalarAsync<decimal>("SELECT SUM(s.SALDO) FROM VE_CPTES_SALDOS_VENTAS s WHERE LTRIM(RTRIM(s.CUENTA))=@Cliente AND LTRIM(RTRIM(s.TC))=@Tc AND s.SUCURSAL=SUBSTRING(@Comprobante,1,4) AND s.NUMERO=SUBSTRING(@Comprobante,5,8) AND s.LETRA=RIGHT(@Comprobante,1);",d,tx);
                Assert.Equal(d.Saldo-(d.Id==docs[0].Id?12:8),saldo);
            }
            Assert.Equal(0,await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='CB' AND SUCURSAL=SUBSTRING(@Comprobante,1,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='X';",resultado,tx));
            Assert.Equal(resultado,await service.GrabarEnTransaccionAsync(cn,tx,solicitud,default));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ALFACORE_COBRANZA_OPERACION WHERE Operacion=@Operacion;",solicitud,tx));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,solicitud with{Aplicaciones=[new(docs[0].Id,11),new(docs[1].Id,9)]},default));
            var otro=await cn.QueryFirstAsync<CobranzaDocumento>("SELECT TOP(1) ID Id FROM V_MV_CPTE WHERE IDCOMPLEMENTO=0 AND (LTRIM(RTRIM(CUENTA))<>@Cliente OR LTRIM(RTRIM(UNEGOCIO))<>@Unidad) AND TC='FC';",docs[0],tx);
            var mezcla=solicitud with {Operacion=Guid.NewGuid(),MovimientoId=null,Aplicaciones=[new(docs[0].Id,12),new(otro.Id,8)]};
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GrabarEnTransaccionAsync(cn,tx,mezcla,default));
            Assert.Equal("Cobrado",await cn.ExecuteScalarAsync<string>("SELECT EstadoRevision FROM BK_ExtractoMovimiento WHERE MovimientoId=@mov;",new{mov},tx));
        }
        finally {tx.Rollback();}
    }

    private sealed class ActivatorDialog:IComponentActivator
    {
        public CobranzaRapidaDialog? Dialog {get;private set;}
        public IComponent CreateInstance(Type type){var c=(IComponent)Activator.CreateInstance(type)!;if(c is CobranzaRapidaDialog d)Dialog=d;return c;}
    }
    private static string Root()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir is not null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"src/AlfaCore/AlfaCore.csproj")))return dir.FullName;
        throw new InvalidOperationException("No se encontró el repositorio.");
    }
}
