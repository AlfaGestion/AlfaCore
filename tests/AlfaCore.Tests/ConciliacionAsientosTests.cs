using System.Reflection;
using System.Text.Json;
using AlfaCore.Common;
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
public sealed class ConciliacionAsientosTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }
    private static ConciliacionBancariaService Service(string connection,CargaComprobantesArcaRuntime runtime)
    {
        var session=new SessionDto();
        return new(Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
    }
    [Fact]
    public void DefaultsUsanCuentasEspecificasUnicasYNoConfundenImpuestosNiComisionesComerciales()
    {
        ConciliacionAsientoCuenta[] cuentas=[new("BANCOGASTO","Gástos Bancarios"),new("FIN","Gastos Financieros / Intereses"),
            new("GANADO","Intereses ganados"),new("SIR","Retencion bancaria de IIBB"),
            new("COMERCIAL","COMISIONES/EXTRAS"),new("IVA","I.V.A. Debito Fiscal"),new("DDJJ","Ingresos Brutos Saldo DDJJ y Pagos")];
        var reglas=ConciliacionAsientoDefaults.Proponer(cuentas);
        Assert.Equal(11,reglas.Count);
        Assert.DoesNotContain(reglas,r=>r.Contrapartida is "COMERCIAL" or "IVA" or "DDJJ");
        var cfg=new ConciliacionAsientoConfig{Reglas=reglas.ToList()};
        string? Proponer(string categoria,string descripcion)=>reglas.FirstOrDefault(r=>r.Id==
            ConciliacionBancariaService.SugerirReglaAsiento(cfg,categoria,"B",descripcion))?.Contrapartida;
        Assert.Equal("SIR",Proponer("Gasto","Regimen de recaudacion sircreb f"));
        Assert.Equal("SIR",Proponer("Impuesto","Adel iibb tucuman"));
        Assert.Equal("FIN",Proponer("Gasto","Cobro de interes por descubierto"));
        Assert.Equal("BANCOGASTO",Proponer("Comision","Cargo del banco"));
        Assert.Equal("GANADO",Proponer("IngresoFinanciero","Intereses por saldo"));
        Assert.Null(Proponer("Gasto","Impuesto ley 25.413"));
        Assert.Null(Proponer("Gasto","Transferencia a un tercero"));
        Assert.Empty(ConciliacionAsientoDefaults.Proponer([new("X","COMISIONES/EXTRAS")]));
        Assert.DoesNotContain(ConciliacionAsientoDefaults.Proponer([..cuentas,new("OTRO","Gastos Bancarios")]),
            r=>r.Contrapartida is "BANCOGASTO" or "OTRO");
    }

    [Fact]
    public void ReglasPriorizanBancoTextoYNoEligenCuandoHayEmpate()
    {
        var a=new ConciliacionAsientoRegla{Nombre="General",Categoria="Comision",Contrapartida="G"};
        var b=new ConciliacionAsientoRegla{Nombre="Banco",Categoria="Comision",CuentaBanco="B",Texto="comis",Contrapartida="G"};
        var cfg=new ConciliacionAsientoConfig{Reglas=[a,b]};
        Assert.Equal(b.Id,ConciliacionBancariaService.SugerirReglaAsiento(cfg,"Comision","B","COMISION bancaria"));
        Assert.Equal(a.Id,ConciliacionBancariaService.SugerirReglaAsiento(cfg,"Comision","OTRO","COMISION bancaria"));
        cfg.Reglas.Add(new(){Nombre="Otra",Categoria="Comision",CuentaBanco="B",Texto="comis",Contrapartida="I"});
        Assert.Null(ConciliacionBancariaService.SugerirReglaAsiento(cfg,"Comision","B","COMISION bancaria"));
        var catalogo=new ConciliacionAsientoCatalogo(cfg,[new("G","Gasto"),new("I","Impuesto")],[new(){Codigo="B",Descripcion="Banco",Moneda="1"}],[],[],[],"");
        ConciliacionBancariaService.ValidarAsientoConfig(cfg,catalogo);
        b.Categoria="Cobranza";Assert.Throws<InvalidOperationException>(()=>ConciliacionBancariaService.ValidarAsientoConfig(cfg,catalogo));b.Categoria="Comision";
        b.Contrapartida="CLIENTE";Assert.Throws<InvalidOperationException>(()=>ConciliacionBancariaService.ValidarAsientoConfig(cfg,catalogo));
        var solicitud=new ConciliacionAsientoSolicitud(Guid.NewGuid(),1,"hash",DateTime.Today,"1",0,"Comisión",[new("G",100),new("I",21)]);
        Assert.Equal(121,ConciliacionBancariaService.ValidarAsientoImportes(solicitud,-121));
        Assert.Throws<InvalidOperationException>(()=>ConciliacionBancariaService.ValidarAsientoImportes(solicitud,-120));
        Assert.Throws<InvalidOperationException>(()=>ConciliacionBancariaService.ValidarAsientoImportes(solicitud with{Contrapartidas=[new("G",120.999m)]},-120.999m));
    }

    [ConciliacionSqlFact]
    public async Task GeneraCcBalanceadoConCajaUsuarioConciliacionAtomicaYProteccionContraDuplicadosConRollback()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var service=Service(connection,runtime);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        var tag="ENSAYO_ASIENTO_"+Guid.NewGuid().ToString("N");long import=0;
        try
        {
            var catalogo=await service.AsientoCatalogoEnConexionAsync(cn,tx,default);
            var banco=catalogo.Bancos.First().Codigo;var cuentas=catalogo.Cuentas.Take(2).ToArray();Assert.Equal(2,cuentas.Length);
            var unidad=catalogo.UnidadInicial;Assert.False(string.IsNullOrWhiteSpace(unidad));
            var tal=catalogo.Talonarios.First(t=>t.Unidad==unidad||t.Unidad is "" or "0").Talonario;
            var contador=await cn.ExecuteScalarAsync<int>("SELECT X_ULTIMO_NRO FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CC' AND Talonario=@tal;",new{tal},tx);
            var cfg=new ConciliacionAsientoConfig{Reglas=[new(){Nombre="Comisión de ensayo",Categoria="NoIdentificado",CuentaBanco=banco,Contrapartida=cuentas[0].Codigo,Detalle="Gastos del extracto"}]};
            ConciliacionBancariaService.ValidarAsientoConfig(cfg,catalogo);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET ValorAux=@json WHERE CLAVE='CONCILIACION-ASIENTOS';",new{json=JsonSerializer.Serialize(cfg)},tx);
            var guardado=await service.AsientoCatalogoEnConexionAsync(cn,tx,default);
            Assert.Empty(guardado.SugerenciasIniciales);
            Assert.Equal(cfg.Reglas[0].Id,Assert.Single(guardado.Config.Reglas).Id);
            import=await cn.ExecuteScalarAsync<long>("""
                INSERT BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco)
                OUTPUT INSERTED.ImportacionId VALUES('Provincia',@tag,'',SYSUTCDATETIME(),'ENSAYO',0,0,'IMPORTED',@banco);
                """,new{tag,banco},tx);
            async Task<long> Banco(decimal importe)=>await cn.ExecuteScalarAsync<long>("""
                INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe)
                OUTPUT INSERTED.MovimientoId VALUES(@import,'Provincia','20261018',@tag,@importe);
                """,new{import,tag,importe},tx);
            var id=await Banco(-123.45m);
            var prep=await service.PrepararAsientoEnConexionAsync(cn,tx,id,default);
            Assert.Equal(cfg.Reglas[0].Id,prep.ReglaSugerida);
            var req=new ConciliacionAsientoSolicitud(Guid.NewGuid(),id,prep.Movimiento.Huella,new(2026,10,18),unidad,tal,"Comisión e impuesto del banco",
                [new(cuentas[0].Codigo,100),new(cuentas[1].Codigo,23.45m)],cfg.Reglas[0].Id,true);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Huella="vieja"}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Unidad="NO"}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Contrapartidas=[new(banco,123.45m)]}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Contrapartidas=[new(cuentas[0].Codigo,1)]}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Fecha=new(1900,1,1)}));
            var result=await service.GenerarAsientoEnTransaccionAsync(cn,tx,req);
            var retry=await service.GenerarAsientoEnTransaccionAsync(cn,tx,req);Assert.Equal(result.Comprobante,retry.Comprobante);Assert.Equal(result.NumeroAsiento,retry.NumeroAsiento);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Detalle="Cambio de detalle"}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Operacion=Guid.NewGuid()}));
            var argumentos=new{periodo=result.Periodo,mes=result.Mes,asiento=result.NumeroAsiento,tipo=result.TipoRegistro};
            Assert.Equal(3,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",argumentos,tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo;",argumentos,tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo AND (DATALENGTH(TC)<>4 OR DATALENGTH(CUENTA)<>DATALENGTH(RTRIM(CUENTA)) OR CABCUENTA IS NOT NULL OR CABNOMBRE IS NOT NULL);",argumentos,tx));
            var caja=await cn.ExecuteScalarAsync<string>("SELECT LTRIM(RTRIM(ISNULL(IdCaja,''))) FROM TA_USUARIOS WHERE NOMBRE='Alberto';",transaction:tx);
            Assert.Equal(3,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo AND IDCajas=@caja;",new{result.Periodo,mes=result.Mes,asiento=result.NumeroAsiento,tipo=result.TipoRegistro,caja=CodigoPk.Format(string.IsNullOrWhiteSpace(caja)?"1":caja,4)},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ConciliacionBanco WHERE MovimientoId=@id AND Activa=1;",new{id},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo AND Conciliado=1;",argumentos,tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE WHERE TC='CC' AND IDCOMPROBANTE=@Comprobante;",result,tx));
            Assert.True(await service.DesconciliarEnTransaccionAsync(cn,tx,result.ConciliacionId,banco,"Revisión de prueba"));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PrepararAsientoEnConexionAsync(cn,tx,id,default));
            var duplicado=await Banco(-123.45m);var p2=await service.PrepararAsientoEnConexionAsync(cn,tx,duplicado,default);Assert.NotEmpty(p2.PosiblesDuplicados);
            var req2=req with{Operacion=Guid.NewGuid(),MovimientoId=duplicado,Huella=p2.Movimiento.Huella,ConfirmarNuevaRegistracion=false};
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GenerarAsientoEnTransaccionAsync(cn,tx,req2));
            var segundo=await service.GenerarAsientoEnTransaccionAsync(cn,tx,req2 with{ConfirmarNuevaRegistracion=true});Assert.NotEqual(result.Comprobante,segundo.Comprobante);
            var ingreso=await Banco(71.31m);var p3=await service.PrepararAsientoEnConexionAsync(cn,tx,ingreso,default);
            var tercero=await service.GenerarAsientoEnTransaccionAsync(cn,tx,req with{Operacion=Guid.NewGuid(),MovimientoId=ingreso,Huella=p3.Movimiento.Huella,Contrapartidas=[new(cuentas[0].Codigo,71.31m)]});
            Assert.Equal("D",await cn.ExecuteScalarAsync<string>("SELECT [DEBE-HABER] FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND TIPO_REG=@tipo AND SECUENCIA=1;",new{periodo=tercero.Periodo,mes=tercero.Mes,asiento=tercero.NumeroAsiento,tipo=tercero.TipoRegistro},tx));
            Assert.True(await cn.ExecuteScalarAsync<int>("SELECT X_ULTIMO_NRO FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CC' AND Talonario=@tal;",new{tal},tx)>contador);
        }
        finally { tx.Rollback(); }
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ImportacionExtracto WHERE ImportacionId=@import;",new{import}));
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE DETALLE='Comisión e impuesto del banco';"));
    }

    [ConciliacionSqlFact]
    public async Task DialogosMuestranCuentasVistaPreviaYReglasEnTresAnchosSinGrabar()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var service=Service(connection,runtime);
        var cat=await service.AsientoCatalogoAsync();var cuenta=cat.Cuentas.First();var banco=cat.Bancos.First().Codigo;
        var regla=new ConciliacionAsientoRegla{Nombre="Comisión de prueba",Categoria="Comision",Contrapartida=cuenta.Codigo,Detalle="Comisión del banco"};
        cat=cat with{Config=new(){Reglas=[regla]}};
        var prep=new ConciliacionAsientoPreparacion(new(){Id=999,Importe=-121,Fecha=new(2026,10,18),Detalle="Comisión bancaria",Huella="prueba"},banco,"Comision",cat,regla.Id,[]);
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);services.AddSingleton(runtime.Events);services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var editor=await renderer.Dispatcher.InvokeAsync(async()=>{var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.ConciliacionAsientoDialog>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Preparacion",prep}}));return root.ToHtmlString();});
        var config=await renderer.Dispatcher.InvokeAsync(async()=>{var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.ConciliacionAsientoConfigDialog>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Catalogo",cat}}));return root.ToHtmlString();});
        Assert.Contains("Confirmar asiento y conciliación",System.Net.WebUtility.HtmlDecode(editor));Assert.Contains("Comisión del banco",System.Net.WebUtility.HtmlDecode(editor));
        Assert.Contains("Guardar cuentas y conceptos",System.Net.WebUtility.HtmlDecode(config));
        var vacio=cat with{Config=new(),SugerenciasIniciales=ConciliacionAsientoDefaults.Proponer(cat.Cuentas)};
        Assert.NotEmpty(vacio.SugerenciasIniciales);
        var inicial=await renderer.Dispatcher.InvokeAsync(async()=>{var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.ConciliacionAsientoConfigDialog>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Catalogo",vacio}}));return root.ToHtmlString();});
        Assert.Contains("Propuesta inicial según el plan de cuentas",System.Net.WebUtility.HtmlDecode(inicial));
        Assert.Contains("SIRCREB",inicial);
        Assert.Empty(vacio.Config.Reglas); // Renderizar no activa ni modifica la configuración vigente.
        if(CargaComprobantesBrowserFactAttribute.BrowserPath is not { } browserPath)return;
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"src/AlfaCore/AlfaCore.csproj")))repo=repo.Parent;Assert.NotNull(repo);
        var css=await File.ReadAllTextAsync(Path.Combine(repo!.FullName,"src/AlfaCore/wwwroot/bootstrap/bootstrap.min.css"))+
            await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/app.css"))+
            await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/css/alfacore-design.css"))+
            await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
        using var pw=await Playwright.CreateAsync();await using var browser=await pw.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=browserPath});var page=await browser.NewPageAsync();
        foreach(var width in new[]{2048,1440,1024})foreach(var html in new[]{editor,config,inicial}) {
            await page.SetViewportSizeAsync(width,1000);await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style><style>body{{margin:0}}*{{box-sizing:border-box}}</style></head><body>{html}</body></html>");
            Assert.True(await page.GetByRole(AriaRole.Dialog).IsVisibleAsync());Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth<=window.innerWidth"));
            if(html==editor)Assert.True(await page.GetByRole(AriaRole.Button,new(){Name="Confirmar asiento y conciliación",Exact=true}).IsVisibleAsync());
            else Assert.True(await page.GetByRole(AriaRole.Button,new(){Name="Guardar cuentas y conceptos",Exact=true}).IsVisibleAsync());
            var dir=Path.Combine(repo.FullName,"artifacts/conciliacion-asientos");Directory.CreateDirectory(dir);await page.ScreenshotAsync(new(){Path=Path.Combine(dir,$"{(html==editor ? "editor" : html==inicial ? "inicial" : "config")}-{width}.png")});
        }
    }
}
