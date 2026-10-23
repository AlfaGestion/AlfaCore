using System.Reflection;
using Microsoft.Playwright;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class ConciliacionVinculosTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }

    [ConciliacionSqlFact]
    public async Task VinculosMultiplesValidanImportesHuellasMarcasIdempotenciaYDesconciliacionConRollback()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var session=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch { "GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null });
        var username="Alberto";
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>username,_=>null });
        var servicio=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        var tag="ENSAYO_CONC_"+Guid.NewGuid().ToString("N");
        var periodo=await cn.QuerySingleAsync<string>("SELECT PERIODO FROM MV_EJERCICIOS WHERE [FECHA DESDE]<='20261018' AND [FECHA HASTA]>='20261018' AND ISNULL(CERRADO,0)=0;",transaction:tx);
        var asiento=await cn.ExecuteScalarAsync<int>("SELECT ISNULL(MAX([NUMERO ASIENTO]),0)+1000 FROM MV_ASIENTOS WHERE PERIODO=@periodo AND MES_OPERATIVO=10;",new{periodo},tx);
        var cuenta=await cn.QueryFirstAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='DB' AND LTRIM(RTRIM(MONEDA))='1' AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0;",transaction:tx);
        var fecha=new DateTime(2026,10,18);
        var import=await cn.ExecuteScalarAsync<long>("""
            INSERT BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco)
            OUTPUT INSERTED.ImportacionId VALUES('Provincia',@tag,'',SYSUTCDATETIME(),'ENSAYO',0,0,'IMPORTED',@cuenta);
            """,new{tag,cuenta},tx);
        async Task<long> Banco(decimal importe)=>await cn.ExecuteScalarAsync<long>("""
            INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe)
            OUTPUT INSERTED.MovimientoId VALUES(@import,'Provincia',@fecha,@tag,@importe);
            """,new{import,fecha,tag,importe},tx);
        async Task Alfa(int sec,decimal importe,string signo="D",bool legacy=false,string moneda="1")=>await cn.ExecuteAsync("""
            INSERT MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,
                [DEBE-HABER],IMPORTE,MONEDA,PERIODO,CABIMPORTE,TIPO_REG,UNEGOCIO,Conciliado)
            VALUES(@cuenta,@sec,10,@asiento,@fecha,@tag,'CB','9999','99999999','X',@signo,@importe,@moneda,@periodo,@importe,'CB','   1',@legacy);
            """,new{cuenta,sec,fecha,tag,signo,importe,moneda=AlfaCore.Common.CodigoPk.Format(moneda,4),periodo,asiento,legacy},tx);
        try
        {
            await cn.ExecuteAsync("""
                IF EXISTS(SELECT 1 FROM TA_CONFIGURACION WHERE CLAVE='CONCILIACION-TOLERANCIA-BANCO')
                    UPDATE TA_CONFIGURACION SET VALOR='0' WHERE CLAVE='CONCILIACION-TOLERANCIA-BANCO';
                ELSE INSERT TA_CONFIGURACION(GRUPO,CLAVE,VALOR) VALUES('CAJA','CONCILIACION-TOLERANCIA-BANCO','0');
                IF EXISTS(SELECT 1 FROM TA_CONFIGURACION WHERE CLAVE='CONCILIACION-DIAS-BANCO')
                    UPDATE TA_CONFIGURACION SET VALOR='3' WHERE CLAVE='CONCILIACION-DIAS-BANCO';
                ELSE INSERT TA_CONFIGURACION(GRUPO,CLAVE,VALOR) VALUES('CAJA','CONCILIACION-DIAS-BANCO','3');
                """,transaction:tx);
            var b1=await Banco(50000m);var b2=await Banco(25123.45m);
            await Alfa(1,30000m);await Alfa(2,45123.45m);
            var f=new ConciliacionCompararFiltro(cuenta,fecha,fecha,"Todos",tag);
            var data=await servicio.CompararEnConexionAsync(cn,tx,f);
            Assert.Equal(2,data.Banco.Count);Assert.Equal(2,data.Alfa.Count);
            var request=new ConciliacionVincularSolicitud(Guid.NewGuid(),f,
                data.Banco.Select(b=>new ConciliacionBancoSeleccion(b.Id,b.Huella)).ToList(),
                data.Alfa.Select(a=>new ConciliacionAlfaSeleccion(a.Clave,a.Huella)).ToList());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,request with { Banco=[request.Banco[0]] }));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,request with { Banco=[request.Banco[0],request.Banco[0]] }));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,request with { Alfa=[request.Alfa[0] with { Huella="vieja" },request.Alfa[1]] }));
            var id=await servicio.ConciliarEnTransaccionAsync(cn,tx,request);
            Assert.Equal(id,await servicio.ConciliarEnTransaccionAsync(cn,tx,request));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,request with{Operacion=Guid.NewGuid()}));
            data=await servicio.CompararEnConexionAsync(cn,tx,f);
            Assert.All(data.Banco,b=>Assert.Equal(id,b.ConciliacionId));Assert.All(data.Alfa,a=>{Assert.Equal(id,a.ConciliacionId);Assert.False(a.Alterado);});
            Assert.Empty((await servicio.CompararEnConexionAsync(cn,tx,f with { Estado="Pendientes" })).Banco);
            Assert.Empty((await servicio.CompararEnConexionAsync(cn,tx,f with { Estado="Pendientes" })).Alfa);
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND Conciliado=1;",new{periodo,asiento},tx));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.DesconciliarEnTransaccionAsync(cn,tx,id,cuenta,""));
            await cn.ExecuteAsync("UPDATE MV_ASIENTOS SET IMPORTE=IMPORTE+1 WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND SECUENCIA=1;",new{periodo,asiento},tx);
            Assert.Contains((await servicio.CompararEnConexionAsync(cn,tx,f)).Alfa,a=>a.Alterado);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.DesconciliarEnTransaccionAsync(cn,tx,id,cuenta,"Corrección de prueba"));
            await cn.ExecuteAsync("UPDATE MV_ASIENTOS SET IMPORTE=IMPORTE-1 WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND SECUENCIA=1;",new{periodo,asiento},tx);
            Assert.True(await servicio.DesconciliarEnTransaccionAsync(cn,tx,id,cuenta,"Corrección de prueba"));
            Assert.False(await servicio.DesconciliarEnTransaccionAsync(cn,tx,id,cuenta,"Reintento de prueba"));
            Assert.Equal("Corrección de prueba",await cn.ExecuteScalarAsync<string>("SELECT Motivo FROM BK_Conciliacion WHERE Id=@id;",new{id},tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND Conciliado=1;",new{periodo,asiento},tx));
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ConciliacionBanco WHERE ConciliacionId=@id AND Activa=0;",new{id},tx));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,request));
            var nuevo=await servicio.ConciliarEnTransaccionAsync(cn,tx,request with { Operacion=Guid.NewGuid() });Assert.NotEqual(id,nuevo);
            await servicio.DesconciliarEnTransaccionAsync(cn,tx,nuevo,cuenta,"Segundo ensayo");
            // Las marcas anteriores de Alfa Gestión no pueden reutilizarse.
            await cn.ExecuteAsync("UPDATE MV_ASIENTOS SET Conciliado=1 WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND SECUENCIA=1;",new{periodo,asiento},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,request with { Operacion=Guid.NewGuid() }));
            await cn.ExecuteAsync("UPDATE MV_ASIENTOS SET Conciliado=0 WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento;",new{periodo,asiento},tx);
            // Sugerencias ambiguas, débitos y monedas incompatibles.
            var b3=await Banco(35.17m);await Alfa(3,35.17m);await Alfa(4,35.17m);
            var bd=await Banco(-18.83m);await Alfa(5,18.83m,"H");
            var bx=await Banco(99.99m);await Alfa(6,99.99m,moneda:"2");
            data=await servicio.CompararEnConexionAsync(cn,tx,f);
            Assert.Equal(2,Assert.Single(data.Sugerencias.Where(s=>s.MovimientoId==b3)).Candidatos);
            Assert.Equal(-18.83m,Assert.Single(data.Sugerencias.Where(s=>s.MovimientoId==bd)).Asiento.Importe);
            Assert.DoesNotContain(data.Sugerencias,s=>s.MovimientoId==bx);
            var extranjero=data.Alfa.Single(a=>a.Secuencia==6);var bancoExtranjero=data.Banco.Single(b=>b.Id==bx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,new(Guid.NewGuid(),f,
                [new(bx,bancoExtranjero.Huella)],[new(extranjero.Clave,extranjero.Huella)])));
            // El filtro de importe usa valor absoluto y el sentido conserva los egresos.
            var egresos=await servicio.CompararEnConexionAsync(cn,tx,f with{Sentido="Egresos",ImporteDesde=18,ImporteHasta=19});
            Assert.Equal(bd,Assert.Single(egresos.Banco).Id);Assert.Equal(-18.83m,Assert.Single(egresos.Alfa).Importe);
            Assert.DoesNotContain(data.Propuestas,p=>p.Banco.Id==b3||p.Banco.Id==bx);
            Assert.Contains(data.Propuestas,p=>p.Banco.Id==bd);
            var repetido=await Banco(-18.83m);
            Assert.DoesNotContain((await servicio.CompararEnConexionAsync(cn,tx,f)).Propuestas,p=>p.Banco.Id==bd||p.Banco.Id==repetido);
            await cn.ExecuteAsync("DELETE BK_ExtractoMovimiento WHERE MovimientoId=@repetido;",new{repetido},tx);
            var positivo=await Banco(45.12m);await Alfa(7,45.12m);
            for(var i=0;i<51;i++)await Banco(72000.1201m+i);
            data=await servicio.CompararEnConexionAsync(cn,tx,f);
            Assert.DoesNotContain(data.Banco,b=>b.Id==bd);
            Assert.Contains(data.Propuestas,p=>p.Banco.Id==bd); // Coincidencias más allá de la página visible.
            var orden=await servicio.CompararEnConexionAsync(cn,tx,f with{OrdenBanco="Importe",DescBanco=false,OrdenAlfa="Importe",DescAlfa=false});
            Assert.Equal(orden.Banco.OrderBy(b=>b.Importe).Select(b=>b.Id),orden.Banco.Select(b=>b.Id));
            Assert.Equal(orden.Alfa.OrderBy(a=>a.Importe).Select(a=>a.Clave),orden.Alfa.Select(a=>a.Clave));
            var cien=await servicio.CompararEnConexionAsync(cn,tx,f with{TamanoPagina=100});
            Assert.Equal(cien.Resumen.Banco,cien.Banco.Count);
            Assert.True(cien.Banco.Count>50);
            Assert.Empty((await servicio.CompararEnConexionAsync(cn,tx,f with{TamanoPagina=100,PaginaBanco=2})).Banco);
            var clasificados=await servicio.CompararEnConexionAsync(cn,tx,f with{Categoria="NoIdentificado",OrdenBanco="Categoria",TamanoPagina=100});
            Assert.All(clasificados.Banco,b=>Assert.Equal("NoIdentificado",b.Categoria));
            Assert.Equal(cien.Banco.Count,clasificados.Banco.Count);
            var sinCobros=await servicio.CompararEnConexionAsync(cn,tx,f with{Categoria="Cobranza"});
            Assert.Empty(sinCobros.Banco);Assert.Equal(data.Resumen.Alfa,sinCobros.Resumen.Alfa);
            await cn.ExecuteAsync("INSERT BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza) VALUES(@bd,'Impuesto',100),(@bd,'Gasto',100);",new{bd},tx);
            var gastos=await servicio.CompararEnConexionAsync(cn,tx,f with{Categoria="Gasto"});
            Assert.Equal(bd,Assert.Single(gastos.Banco).Id);Assert.Equal(data.Resumen.Alfa,gastos.Resumen.Alfa);
            var otroConcepto=await Banco(-18.83m);
            await cn.ExecuteAsync("INSERT BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza) VALUES(@otroConcepto,'Cobranza',100);",new{otroConcepto},tx);
            Assert.DoesNotContain((await servicio.CompararEnConexionAsync(cn,tx,f with{Categoria="Gasto"})).Propuestas,p=>p.Banco.Id==bd);
            await cn.ExecuteAsync("DELETE BK_MovimientoClasificado WHERE MovimientoId=@otroConcepto; DELETE BK_ExtractoMovimiento WHERE MovimientoId=@otroConcepto;",new{otroConcepto},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.CompararEnConexionAsync(cn,tx,f with{Categoria="INVENTADA"}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.CompararEnConexionAsync(cn,tx,f with{TamanoPagina=100000}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.CompararEnConexionAsync(cn,tx,f with{OrdenBanco="Fecha;DROP TABLE x"}));
            // La tolerancia propone centavos, sin modificar las registraciones.
            await Alfa(8,777.22m);var centavos=await Banco(777m);
            Assert.DoesNotContain((await servicio.CompararEnConexionAsync(cn,tx,f)).Propuestas,p=>p.Banco.Id==centavos);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='0.25' WHERE CLAVE='CONCILIACION-TOLERANCIA-BANCO';",transaction:tx);
            Assert.Equal(new ConciliacionCriterios(3,0.25m,true,false),await ConciliacionBancariaService.LeerCriterios(cn,tx,default));
            Assert.Throws<InvalidOperationException>(()=>ConciliacionBancariaService.ValidarCriterios(new(32,0.25m,true,false)));
            var tolerantes=await servicio.CompararEnConexionAsync(cn,tx,f);
            Assert.Equal(0.25m,tolerantes.Resumen.Tolerancia);
            Assert.Equal(777.22m,Assert.Single(tolerantes.Propuestas.Where(p=>p.Banco.Id==centavos)).Alfa.Importe);
            await cn.ExecuteAsync("""
                IF EXISTS(SELECT 1 FROM TA_CONFIGURACION WHERE CLAVE='CONCILIACION-COMPARAR-REFERENCIA')
                    UPDATE TA_CONFIGURACION SET VALOR='SI' WHERE CLAVE='CONCILIACION-COMPARAR-REFERENCIA';
                ELSE INSERT TA_CONFIGURACION(GRUPO,CLAVE,VALOR) VALUES('CAJA','CONCILIACION-COMPARAR-REFERENCIA','SI');
                """,transaction:tx);
            Assert.DoesNotContain((await servicio.CompararEnConexionAsync(cn,tx,f)).Propuestas,p=>p.Banco.Id==centavos);
            await cn.ExecuteAsync("UPDATE BK_ExtractoMovimiento SET ReferenciaExterna='REF-CENTAVOS' WHERE MovimientoId=@centavos; UPDATE MV_ASIENTOS SET NroComprobanteBancario='REF-CENTAVOS' WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND SECUENCIA=8;",new{centavos,periodo,asiento},tx);
            Assert.Contains((await servicio.CompararEnConexionAsync(cn,tx,f)).Propuestas,p=>p.Banco.Id==centavos);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='NO' WHERE CLAVE='CONCILIACION-COMPARAR-REFERENCIA';",transaction:tx);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='0' WHERE CLAVE='CONCILIACION-TOLERANCIA-BANCO';",transaction:tx);
            var lote=data.Propuestas.Where(p=>p.Banco.Id==bd||p.Banco.Id==positivo).Select(p=>new ConciliacionVincularSolicitud(
                Guid.NewGuid(),f,[new(p.Banco.Id,p.Banco.Huella)],[new(p.Alfa.Clave,p.Alfa.Huella)])).ToList();
            Assert.Equal(2,lote.Count);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarLoteEnTransaccionAsync(cn,tx,[lote[0],lote[0]]));
            tx.Save("AntesLote");
            var vencido=lote[1] with{Alfa=[lote[1].Alfa[0] with{Huella="vencida"}]};
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarLoteEnTransaccionAsync(cn,tx,[lote[0],vencido]));
            tx.Rollback("AntesLote");
            var idsLote=await servicio.ConciliarLoteEnTransaccionAsync(cn,tx,lote);
            Assert.Equal(2,idsLote.Distinct().Count());
            Assert.Equal(idsLote,await servicio.ConciliarLoteEnTransaccionAsync(cn,tx,lote));
            var luego=await servicio.CompararEnConexionAsync(cn,tx,f with{Estado="Pendientes"});
            Assert.DoesNotContain(luego.Propuestas,p=>p.Banco.Id==bd||p.Banco.Id==positivo);
            var conCentavos=(await servicio.CompararEnConexionAsync(cn,tx,f)).Banco.Single(b=>b.Id==centavos);
            var asientoCentavos=(await servicio.CompararEnConexionAsync(cn,tx,f)).Alfa.Single(a=>a.Secuencia==8);
            var reqCentavos=new ConciliacionVincularSolicitud(Guid.NewGuid(),f,[new(centavos,conCentavos.Huella)],[new(asientoCentavos.Clave,asientoCentavos.Huella)],true,"Redondeo del extracto");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,reqCentavos)); // Tolerancia cero.
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='0.25' WHERE CLAVE='CONCILIACION-TOLERANCIA-BANCO';",transaction:tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,reqCentavos with{AceptarDiferencia=false}));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.ConciliarEnTransaccionAsync(cn,tx,reqCentavos with{MotivoDiferencia=""}));
            var idCentavos=await servicio.ConciliarEnTransaccionAsync(cn,tx,reqCentavos);
            Assert.Equal(idCentavos,await servicio.ConciliarEnTransaccionAsync(cn,tx,reqCentavos));
            var auditoria=await cn.QuerySingleAsync<ConciliacionVinculoCabecera>("SELECT * FROM BK_Conciliacion WHERE Id=@id;",new{id=idCentavos},tx);
            Assert.Equal(-0.22m,auditoria.DiferenciaImporte);Assert.Equal(0.25m,auditoria.ToleranciaImporte);Assert.Equal("Redondeo del extracto",auditoria.MotivoDiferencia);
            Assert.Equal(777m,await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE FROM BK_ExtractoMovimiento WHERE MovimientoId=@centavos;",new{centavos},tx));
            Assert.Equal(777.22m,await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE FROM MV_ASIENTOS WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento AND SECUENCIA=8;",new{periodo,asiento},tx));
            Assert.True(await servicio.DesconciliarEnTransaccionAsync(cn,tx,idCentavos,cuenta,"Revisión de prueba"));
            Assert.Equal("Redondeo del extracto",await cn.ExecuteScalarAsync<string>("SELECT MotivoDiferencia FROM BK_Conciliacion WHERE Id=@id;",new{id=idCentavos},tx));
            var literal=await servicio.CompararEnConexionAsync(cn,tx,f with { Buscar="__NO_EXISTE_%" });Assert.Empty(literal.Banco);Assert.Empty(literal.Alfa);
            username="USUARIO_NO_EXISTENTE_CONCILIACION";
            await Assert.ThrowsAsync<SqlException>(()=>servicio.CompararEnConexionAsync(cn,tx,f));
        }
        finally { tx.Rollback(); }
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ImportacionExtracto WHERE ImportacionId=@import;",new{import}));
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE PERIODO=@periodo AND [NUMERO ASIENTO]=@asiento;",new{periodo,asiento}));
    }

    [ConciliacionSqlFact]
    public async Task ComparacionRealRespetaPermisosYCuentasYRenderizaFiltrosSinEscribir()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var session=new SessionDto();
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch { "GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null });
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null });
        var permission=true;
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(permission)),runtime.Events,new());
        var cuentas=await service.CuentasAsync();
        var cuenta=(cuentas.FirstOrDefault(c=>c.Codigo=="111020303") ?? cuentas.First(c=>c.Moneda=="1")).Codigo;
        var filtro=new ConciliacionCompararFiltro(cuenta,new(2026,10,1),new(2026,10,31),"Todos");
        var data=await service.CompararAsync(filtro);
        Assert.InRange(data.Banco.Count,0,50);Assert.InRange(data.Alfa.Count,0,50);
        Assert.All(data.Sugerencias,s=>Assert.True(s.Candidatos>0));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.CompararAsync(filtro with{Cuenta="NO_EXISTE"}));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.CompararAsync(filtro with{Hasta=new(2028,1,1)}));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.VinculoAsync(-1));
        Assert.Empty(await service.HistorialBancoAsync(-1));
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(service);services.AddSingleton(runtime.Events);
        services.AddSingleton<IAppUiOperationService,AppUiOperationService>();
        services.AddSingleton(Proxy<Microsoft.JSInterop.IJSRuntime>((_,_)=>null));
        var activador=new ActivadorComparacion();services.AddSingleton<IComponentActivator>(activador);
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        string browseHtml="",propuestasHtml="",configuracionHtml="";
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<AlfaCore.Components.Shared.ConciliacionComparacionPanel>(
                ParameterView.FromDictionary(new Dictionary<string,object?> { ["CuentaInicial"]=cuenta,
                    ["OnGenerarAsiento"]=EventCallback.Factory.Create<long>(this,(long _)=>{}) }));
            var panel=activador.Panel!;
            void Field(string name,object value) {
                if(panel.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance) is { } field)field.SetValue(panel,value);
                else panel.GetType().GetProperty(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(panel,value);
            }
            Field("_desde","2026-10-01");Field("_hasta","2026-10-31");Field("_estado","Todos");
            await (Task)panel.GetType().GetMethod("Consultar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,null)!;
            var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Movimientos del banco",html);Assert.Contains("Movimientos registrados en Alfa",html);
            Assert.Contains("Conciliar selección",html);
            Assert.Contains("Generar asiento",html);
            var acciones=(IReadOnlyList<PageHeaderAction>)panel.GetType().GetMethod("Acciones",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,null)!;
            Assert.Contains(acciones,a=>a.Key=="asistir-ia"&&a.Label=="Asistir con IA");
            Assert.Contains(acciones,a=>a.Key=="generar-asiento");
            Assert.Contains("Buscar descripción",html);Assert.Contains("Ordenar banco por",html);
            var search=activador.Search!;
            search.GetType().GetMethod("Toggle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(search,null);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(search,null);
            var filtrosHtml=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Clasificación del extracto",filtrosHtml);Assert.Contains("Registros por página",filtrosHtml);
            search.GetType().GetMethod("Toggle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(search,null);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(search,null);
            await (Task)panel.GetType().GetMethod("Ordenar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,[true,"Importe"])!;
            var aplicado=(ConciliacionCompararFiltro)panel.GetType().GetField("_aplicado",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(panel)!;
            Assert.Equal("Importe",aplicado.OrdenBanco);Assert.False(aplicado.DescBanco);Assert.Equal(1,aplicado.PaginaBanco);
            await (Task)panel.GetType().GetMethod("Ordenar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,[true,"Importe"])!;
            aplicado=(ConciliacionCompararFiltro)panel.GetType().GetField("_aplicado",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(panel)!;
            Assert.True(aplicado.DescBanco);
            browseHtml=root.ToHtmlString();
            panel.GetType().GetMethod("AbrirPropuestas",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,null);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,null);
            propuestasHtml=root.ToHtmlString();
            Assert.Contains("Propuestas de conciliación del período",System.Net.WebUtility.HtmlDecode(propuestasHtml));
            Field("_propuestasAbiertas",false);
            Field("_configurar",true);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,null);
            configuracionHtml=root.ToHtmlString();
            Field("_configurar",false);
            if(data.Alfa.FirstOrDefault() is { } primero)Assert.Contains(primero.Comprobante,html);
            GuardarVista("comparacion-banco.html",root.ToHtmlString());
            if(data.Sugerencias.FirstOrDefault() is { } s)
            {
                var b=data.Banco.Single(b=>b.Id==s.MovimientoId);
                panel.GetType().GetMethod("UsarSugerencia",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,[b,s]);
                typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,null);
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("Confirmar conciliación",html);Assert.Contains("role=\"dialog\"",html);
                Assert.Contains(s.Asiento.Comprobante,html);GuardarVista("comparacion-confirmar.html",root.ToHtmlString());
            }
            if(data.Banco.FirstOrDefault() is { } banco)
            {
                Field("_confirmar",false);
                await (Task)panel.GetType().GetMethod("Historial",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,[banco.Id])!;
                html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("Historial de conciliación",html);Assert.Contains("role=\"dialog\"",html);
                GuardarVista("comparacion-historial.html",root.ToHtmlString());
            }
        });
        if(CargaComprobantesBrowserFactAttribute.BrowserPath is { } browserPath)
        {
            var repo=new DirectoryInfo(AppContext.BaseDirectory);
            while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"src/AlfaCore/AlfaCore.csproj")))repo=repo.Parent;
            Assert.NotNull(repo);
            var css=await File.ReadAllTextAsync(Path.Combine(repo!.FullName,"src/AlfaCore/wwwroot/bootstrap/bootstrap.min.css"))+
                await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/css/gestion-alfadesign.css"))+
                await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/css/modulos-alfadesign.css"))+
                await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/css/alfacore-design.css"))+
                await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/wwwroot/app.css"))+
                await File.ReadAllTextAsync(Path.Combine(repo.FullName,"src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
            using var playwright=await Playwright.CreateAsync();
            await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,ExecutablePath=browserPath});
            var page=await browser.NewPageAsync();
            foreach(var width in new[]{2048,1440,1024})
            {
                await page.SetViewportSizeAsync(width,1000);
                foreach(var view in new[]{browseHtml,propuestasHtml,configuracionHtml})
                {
                    await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style><style>body{{margin:0;font-family:Segoe UI,sans-serif;height:100dvh}}*{{box-sizing:border-box}}</style></head><body><div class='shell shell--alfa-design' style='height:100dvh;display:block'>{view}</div></body></html>");
                    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
                    if(view==propuestasHtml||view==configuracionHtml)Assert.True(await page.GetByRole(AriaRole.Dialog).IsVisibleAsync());
                    else {
                        Assert.True(await page.GetByRole(AriaRole.Button,new(){Name="Ordenar banco por Importe"}).IsVisibleAsync());
                        Assert.Equal(0,await page.GetByLabel("Ordenar banco por",new(){Exact=true}).CountAsync());
                        foreach(var list in await page.Locator(".conciliacion-comparacion__lista").AllAsync()) {
                            var scroll=list.Locator(".conciliacion-comparacion__scroll");
                            var title=await list.Locator("h3").BoundingBoxAsync();
                            var before=await scroll.Locator("th").First.BoundingBoxAsync();
                            await scroll.EvaluateAsync("e=>e.scrollTop=e.scrollHeight");
                            var after=await scroll.Locator("th").First.BoundingBoxAsync();
                            var titleAfter=await list.Locator("h3").BoundingBoxAsync();
                            Assert.NotNull(before);Assert.NotNull(after);Assert.NotNull(title);Assert.NotNull(titleAfter);
                            Assert.InRange(Math.Abs(before!.Y-after!.Y),0,1);Assert.InRange(Math.Abs(title!.Y-titleAfter!.Y),0,1);
                        }
                    }
                    if(view==configuracionHtml)Assert.True(await page.GetByLabel("Tolerancia de importe ($)",new(){Exact=true}).IsVisibleAsync());
                    var visual=Path.Combine(repo.FullName,"artifacts/conciliacion-criterios");Directory.CreateDirectory(visual);
                    var name=view==browseHtml ? "comparacion" : view==configuracionHtml ? "criterios" : "propuestas";
                    await page.ScreenshotAsync(new(){Path=Path.Combine(visual,$"{name}-{width}.png")});
                }
            }
        }
        permission=false;
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.CompararAsync(filtro));
    }

    private static void GuardarVista(string nombre,string html)
    {
        var dir=Environment.GetEnvironmentVariable("CONCILIACION_VISUAL_DIR");if(string.IsNullOrWhiteSpace(dir))return;
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,nombre),html,new System.Text.UTF8Encoding(false));
    }
    private sealed class ActivadorComparacion:IComponentActivator
    {
        public AlfaCore.Components.Shared.ConciliacionComparacionPanel? Panel { get;private set; }
        public AlfaCore.Components.Shared.GestionSmartSearch? Search { get;private set; }
        public IComponent CreateInstance(Type type)
        {
            var c=(IComponent)Activator.CreateInstance(type)!;
            if(c is AlfaCore.Components.Shared.ConciliacionComparacionPanel p)Panel=p;
            if(c is AlfaCore.Components.Shared.GestionSmartSearch s)Search=s;
            return c;
        }
    }
}

[CollectionDefinition("Conciliacion SQL",DisableParallelization=true)]
public sealed class ConciliacionSqlCollection;
