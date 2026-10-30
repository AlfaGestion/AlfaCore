using System.Data;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasConfirmarVariasTests
{
    private static readonly TarjetasFiltro Filtro=new(1,new(2026,4,1),new(2026,4,30));
    private static TarjetasComparacion Datos()=>new(new(){Id=1,Cuenta="TJ",DetalleCompleto=true},
        [new(){Id=10,Importe=100.01m},new(){Id=20,Importe=199.99m}],
        [new(){Clave="A",Huella="HA",Importe=100},new(){Clave="B",Huella="HB",Importe=200}],1);
    private static TarjetasConfirmarVarias Solicitud()=>new(Filtro,
        [new([10],[new("A","HA")],"Alta: par uno"),new([20],[new("B","HB")],"Media: par dos")],true,"Diferencias de centavos aceptadas");

    [Fact]
    public void MantieneGruposSeparadosYExigeAceptacionAunqueLasDiferenciasSeCompensen()
    {
        var datos=Datos();var solicitud=Solicitud();
        var grupos=ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud);
        Assert.Equal(2,grupos.Count);Assert.Single(grupos[0].Grupo.Detalles);Assert.Single(grupos[1].Grupo.Movimientos);
        Assert.Contains("centavos",grupos[0].Motivo);Assert.Contains("par dos",grupos[1].Motivo);
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud with{AceptarDiferencias=false}));
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud with{Motivo=" "}));
        datos.Detalles[0].Importe=101.01m;
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud));
    }
    [Fact]
    public void RevalidaTodosLosGruposYRechazaReusoCambiosYLiquidacionesPrevias()
    {
        var datos=Datos();var solicitud=Solicitud();
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud with{Propuestas=[solicitud.Propuestas[0],solicitud.Propuestas[0]]}));
        datos.Movimientos[1].Huella="CAMBIO";
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud));
        datos.Movimientos[1].Huella="HB";datos.Movimientos[1].LegacyLiquidado=true;
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud));
        datos.Movimientos[1].LegacyLiquidado=false;datos.Detalles[1].Vinculo=99;
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud));
    }
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }
    [TarjetasVariasSqlFact]
    public async Task GuardaVinculosIndependientesConMotivoSinCambiarContabilidadYRollback()
    {
        var connection=Environment.GetEnvironmentVariable("TARJETAS_VARIAS_TEST_SQL")!;
        Assert.Contains(new SqlConnectionStringBuilder(connection).InitialCatalog,new[]{"ELBOLSON2011_PRUEBAS","PRUEBAS02122025"});
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var service=new ConciliacionTarjetasService(Proxy<ISessionService>((m,_)=>m.Name=="GetActiveSession"?sesion:null),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"SISTEMAS",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var tag="ENSAYO_VARIAS_"+Guid.NewGuid().ToString("N");long id=0;
        try
        {
            var origen=await cn.QueryFirstOrDefaultAsync<(string Cuenta,DateTime Desde,DateTime Hasta)>("SELECT TOP(1) Cuenta,Desde,Hasta FROM dbo.TJ_Importacion ORDER BY Id DESC",transaction:tx);
            if(string.IsNullOrWhiteSpace(origen.Cuenta))
            {
                // El ensayo no depende de que el operador conserve importaciones de prueba.
                // Solo se lee la contabilidad para elegir la fecha/cuenta del lote sintético.
                origen=await cn.QueryFirstAsync<(string Cuenta,DateTime Desde,DateTime Hasta)>("""
                    SELECT TOP(1) LTRIM(RTRIM(a.CUENTA)) Cuenta,a.FECHA Desde,a.FECHA Hasta
                    FROM dbo.MV_ASIENTOS a
                    WHERE a.[DEBE-HABER]='D' AND a.IMPORTE>0 AND LTRIM(RTRIM(a.MONEDA))='1'
                      AND ISNULL(a.EsResumen,0)=0 AND ISNULL(a.EsCierre,0)=0 AND ISNULL(a.ES_SALDO_APERTURA,0)=0
                      AND EXISTS(SELECT 1 FROM dbo.MA_CUENTAS m WHERE LTRIM(RTRIM(m.CODIGO))=LTRIM(RTRIM(a.CUENTA))
                        AND LTRIM(RTRIM(m.MEDIODEPAGO))='TJ' AND ISNULL(m.TITULO,0)=0 AND ISNULL(m.Dada_De_Baja,0)=0)
                      AND EXISTS(SELECT 1 FROM dbo.V_TA_Cpte t WHERE LTRIM(RTRIM(t.Codigo))=LTRIM(RTRIM(a.TC)) AND LTRIM(RTRIM(t.SISTEMA))='Ventas')
                    ORDER BY a.FECHA DESC;
                    """,transaction:tx);
            }
            var cuenta=origen.Cuenta;
            var args=new{cuenta,desde=origen.Desde.Date.AddDays(-3),hasta=origen.Hasta.Date.AddDays(3),sucursal="",usuario="SISTEMAS"};
            var antes=(await cn.QueryAsync<TarjetasMovimiento>(ConciliacionTarjetasService.MovimientosAlfaSql,args,tx)).AsList();
            var usados=(await cn.QueryAsync<string>("SELECT Clave FROM dbo.TJ_VinculoAlfa WHERE Cuenta=@cuenta AND Activo=1",new{cuenta},tx)).ToHashSet();
            var ms=antes.Where(m=>!m.LegacyLiquidado&&!usados.Contains(m.Clave)&&!usados.Contains(m.ClaveAnterior)).Take(2).ToList();Assert.Equal(2,ms.Count);
            var tolerancia=decimal.Parse((await cn.ExecuteScalarAsync<string>("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE='TARJETAS-TOLERANCIA'",transaction:tx))!,System.Globalization.CultureInfo.InvariantCulture);
            var delta=Math.Min(tolerancia,0.01m);
            id=await cn.ExecuteScalarAsync<long>("""
                INSERT dbo.TJ_Importacion(Nombre,Original,Hash,Banco,Comercio,Tarjeta,Cuenta,Sucursal,Desde,Hasta,Bruto,Neto,DetalleCompleto,Avisos,Usuario)
                OUTPUT INSERTED.Id VALUES(@tag,0x25504446,@hash,'Ensayo',@tag,'VISA',@cuenta,'',@desde,@hasta,@total,@total,1,'','SISTEMAS');
                """,new{tag,hash=Guid.NewGuid().ToString("N").PadRight(64,'0'),cuenta,args.desde,args.hasta,total=ms.Sum(m=>m.Importe)},tx);
            var selecciones=new List<TarjetasIaSeleccion>();
            for(var i=0;i<ms.Count;i++)
            {
                var m=ms[i];var detalle=await cn.ExecuteScalarAsync<long>("""
                    INSERT dbo.TJ_Detalle(ImportacionId,Fecha,Liquidacion,Lote,Cupon,Terminal,Nivel,Cantidad,Importe,Huella)
                    OUTPUT INSERTED.Id VALUES(@id,@Fecha,'1','1','','','Cupón',1,@importe,@hash);
                    """,new{id,m.Fecha,importe=m.Importe+(i==0?delta:-delta),hash=Guid.NewGuid().ToString("N").PadRight(64,'0')},tx);
                selecciones.Add(new([detalle],[new(m.Clave,m.Huella)],"Alta: evidencia de ensayo "+i));
            }
            var solicitud=new TarjetasConfirmarVarias(new(id,args.desde,args.hasta),selecciones,true,"Centavos aceptados sin cambiar importes");
            if(delta>0)await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmarVariasEnTransaccionAsync(cn,tx,solicitud with{AceptarDiferencias=false},default));
            var invalida=solicitud with{Propuestas=[selecciones[0],selecciones[1] with{Movimientos=[new(ms[1].Clave,"HUELLA_OBSOLETA")]}]};
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmarVariasEnTransaccionAsync(cn,tx,invalida,default));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Vinculo WHERE ImportacionId=@id",new{id},tx));
            var ids=await service.ConfirmarVariasEnTransaccionAsync(cn,tx,solicitud,default);
            Assert.Equal(2,ids.Count);Assert.NotEqual(ids[0],ids[1]);
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Vinculo WHERE ImportacionId=@id AND Activo=1 AND Motivo LIKE '%Centavos aceptados%'",new{id},tx));
            foreach(var vinculo in ids)
            {
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_VinculoDetalle WHERE VinculoId=@vinculo",new{vinculo},tx));
                Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_VinculoAlfa WHERE VinculoId=@vinculo",new{vinculo},tx));
            }
            var deshacer=new TarjetasDeshacerVarias(id,cuenta,ids,"Revertir vínculos sintéticos del ensayo");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.DesconciliarEnTransaccion(cn,tx,
                deshacer with{Vinculos=[ids[0],long.MaxValue]},default));
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Vinculo WHERE ImportacionId=@id AND Activo=1",new{id},tx));
            Assert.Equal(2,await service.DesconciliarEnTransaccion(cn,tx,deshacer,default));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Vinculo WHERE ImportacionId=@id AND Activo=1",new{id},tx));
            Assert.Equal(2,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Vinculo WHERE ImportacionId=@id AND Activo=0 AND FechaAnulacion IS NOT NULL AND UsuarioAnulacion='SISTEMAS' AND MotivoAnulacion=@motivo",new{id,motivo=deshacer.Motivo},tx));
            foreach(var vinculo in ids)
            {
                Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_VinculoDetalle WHERE VinculoId=@vinculo AND Activo=1",new{vinculo},tx));
                Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_VinculoAlfa WHERE VinculoId=@vinculo AND Activo=1",new{vinculo},tx));
            }
            var despues=await cn.QueryAsync<TarjetasMovimiento>(ConciliacionTarjetasService.MovimientosAlfaSql,args,tx);
            Assert.Equal(antes.Select(m=>m.Huella),despues.Select(m=>m.Huella));
        }
        finally{tx.Rollback();}
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Importacion WHERE Id=@id",new{id}));
    }
}

public sealed class TarjetasVariasSqlFactAttribute:FactAttribute
{
    public TarjetasVariasSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_VARIAS_TEST_SQL")))Skip="Requiere conexión autorizada a ELBOLSON2011_PRUEBAS o PRUEBAS02122025 en TARJETAS_VARIAS_TEST_SQL.";}
}
