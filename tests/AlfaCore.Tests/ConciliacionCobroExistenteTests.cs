using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class ConciliacionCobroExistenteTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }
    [ConciliacionSqlFact]
    public async Task ReconoceCbLegacySinCrearAsientosNiConciliarYConservaAuditoriaConRollback()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        using var runtime=new CargaComprobantesArcaRuntime();var session=new SessionDto();var username="Alberto";
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch{"GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null});
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>username,_=>null});
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        var real=await service.CuentaCorrienteAsync(548);
        var recibo=real.Coincidencias.Single(c=>c.Comprobante=="000100007539X");
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        var import=await cn.ExecuteScalarAsync<long>("""
            INSERT BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco)
            OUTPUT INSERTED.ImportacionId SELECT OrigenCodigo,'ENSAYO_COBRO_EXISTENTE','',SYSUTCDATETIME(),'ENSAYO',0,0,'IMPORTED',CuentaBanco
            FROM BK_ImportacionExtracto WHERE ImportacionId=(SELECT ImportacionId FROM BK_ExtractoMovimiento WHERE MovimientoId=548);
            """,transaction:tx);
        async Task<long> Copia(long origen=548,string tipo="CobranzaYaRegistrada")=>await cn.ExecuteScalarAsync<long>("""
            INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe,MonedaCodigo)
            SELECT @import,OrigenCodigo,FechaMovimiento,'ENSAYO_COBRO_EXISTENTE',Importe,MonedaCodigo FROM BK_ExtractoMovimiento WHERE MovimientoId=@origen;
            DECLARE @nuevo bigint=SCOPE_IDENTITY();
            INSERT BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza) VALUES(@nuevo,'Cobranza',100);
            INSERT BK_MovimientoMatch(MovimientoId,TipoMatch,ClienteCodigo,Confianza,ResumenPropuesta)
            SELECT TOP(1) @nuevo,@tipo,ClienteCodigo,70,'ENSAYO' FROM BK_MovimientoMatch WHERE MovimientoId=@origen ORDER BY MatchId DESC;
            SELECT @nuevo;
            """,new{import,origen,tipo},tx);
        async Task ClonarRecibo(ConciliacionCtaMovimiento original)
        {
            var origen=original.Clave;
            var asiento=await cn.ExecuteScalarAsync<int>("SELECT MAX([NUMERO ASIENTO])+1000 FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes;",origen,tx);
            var numero=asiento.ToString("D8");
            await cn.ExecuteAsync("""
                INSERT MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,
                    [DEBE-HABER],IMPORTE,MONEDA,PERIODO,CABIMPORTE,TIPO_REG,UNEGOCIO,USUARIO_LOGEADO,IDCAJAS,FechaSubdiario)
                SELECT CUENTA,SECUENCIA,MES_OPERATIVO,@asientoNuevo,FECHA,'ENSAYO_COBRO_EXISTENTE',TC,'9999',@numero,LETRA,
                    [DEBE-HABER],IMPORTE,MONEDA,PERIODO,CABIMPORTE,TIPO_REG,UNEGOCIO,USUARIO_LOGEADO,IDCAJAS,FechaSubdiario
                FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND TIPO_REG=@TipoRegistro;
                """,new{origen.Periodo,origen.Mes,origen.Asiento,origen.TipoRegistro,asientoNuevo=asiento,numero},tx);
            original.Asiento=asiento;original.Comprobante="9999"+numero+"X";
        }
        try
        {
            // Copias transaccionales de los recibos: el usuario puede haber reconocido los reales.
            await ClonarRecibo(recibo);
            var mov=await Copia();var otro=await Copia();
            var importe=recibo.Haber;var comprobante=$"{recibo.Tc}|{recibo.Comprobante}";
            var cantidad=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS;",transaction:tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReconocerCobroEnTransaccionAsync(cn,tx,mov,recibo.Clave,importe+1,comprobante));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReconocerCobroEnTransaccionAsync(cn,tx,mov,recibo.Clave,importe,"CB|OTRO"));
            Assert.True(await service.ReconocerCobroEnTransaccionAsync(cn,tx,mov,recibo.Clave,importe,comprobante));
            Assert.False(await service.ReconocerCobroEnTransaccionAsync(cn,tx,mov,recibo.Clave,importe,comprobante));
            Assert.Equal("Cobrado",await cn.ExecuteScalarAsync<string>("SELECT EstadoRevision FROM BK_ExtractoMovimiento WHERE MovimientoId=@mov;",new{mov},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_GeneracionAlfaLog WHERE MovimientoId=@mov AND Accion='COBRO_EXISTENTE';",new{mov},tx));
            Assert.Equal("Alberto",await cn.ExecuteScalarAsync<string>("SELECT Usuario FROM BK_CobroExistente WHERE MovimientoId=@mov;",new{mov},tx));
            Assert.Equal(cantidad,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS;",transaction:tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ConciliacionBanco WHERE MovimientoId=@mov;",new{mov},tx));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReconocerCobroEnTransaccionAsync(cn,tx,otro,recibo.Clave,importe,comprobante));
            // El Molinero: banco neto distinto del recibo legacy y propuesta Anticipo.
            username="Alberto";
            var mol=await Copia(559,"Anticipo");
            var molRecibo=new ConciliacionCtaMovimiento{Periodo="20",Mes=10,Asiento=17,TipoRegistro="CB",Secuencia=1};
            await ClonarRecibo(molRecibo);var molClave=molRecibo.Clave;var molComprobante="CB|"+molRecibo.Comprobante;
            cantidad=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS;",transaction:tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReconocerCobroEnTransaccionAsync(cn,tx,mol,molClave,397428.87m,molComprobante));
            Assert.True(await service.ReconocerCobroEnTransaccionAsync(cn,tx,mol,molClave,397428.87m,molComprobante,observacion:"Cobro confirmado por el usuario; diferencia pendiente de revisar"));
            Assert.Equal(389332.86m,await cn.ExecuteScalarAsync<decimal>("SELECT ImporteBanco FROM BK_CobroExistente WHERE MovimientoId=@mol;",new{mol},tx));
            Assert.Equal(397428.87m,await cn.ExecuteScalarAsync<decimal>("SELECT Importe FROM BK_CobroExistente WHERE MovimientoId=@mol;",new{mol},tx));
            Assert.False(string.IsNullOrWhiteSpace(await cn.ExecuteScalarAsync<string>("SELECT Observacion FROM BK_CobroExistente WHERE MovimientoId=@mol;",new{mol},tx)));
            Assert.Equal(cantidad,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS;",transaction:tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ConciliacionBanco WHERE MovimientoId=@mol;",new{mol},tx));
            username="USUARIO_SIN_UNIDADES";
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReconocerCobroEnTransaccionAsync(cn,tx,mov,recibo.Clave,importe,comprobante));
        }
        finally { tx.Rollback(); }
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BK_ImportacionExtracto WHERE ImportacionId=@import;",new{import}));
    }
}
