using System.Reflection;
using AlfaCore.ConciliacionBancaria.Infrastructure;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("Conciliacion SQL")]
public sealed class ConciliacionPosibleDuplicadoTests
{
    [ConciliacionSqlFact]
    public async Task AvisoActualDetectaReciboConOtraFechaYRedondeoAunqueLaPropuestaGuardadaSeaAnticipo()
    {
        var connection=Environment.GetEnvironmentVariable("CONCILIACION_TEST_SQL")!;
        Assert.Equal("ALFANET2007_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        var session=new SessionDto();
        T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
        {
            var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
        }
        var sessions=Proxy<ISessionService>((m,_)=>m.Name switch { "GetConnectionString"=>connection,"GetActiveSession"=>session,_=>null });
        var users=Proxy<IAppUserSessionService>((m,_)=>m.Name switch { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Alberto",_=>null });
        using var runtime=new CargaComprobantesArcaRuntime();
        var service=new ConciliacionBancariaService(sessions,users,Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,new());
        var original=(await service.CuentaCorrienteAsync(548)).Coincidencias.First();
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        using var tx=cn.BeginTransaction();
        try
        {
            var import=await cn.ExecuteScalarAsync<long>("""
                INSERT BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso)
                OUTPUT INSERTED.ImportacionId VALUES('MercadoPago','ENSAYO_DUPLICADO','',SYSUTCDATETIME(),'ENSAYO',1,1119185,'IMPORTED');
                """,transaction:tx);
            var cliente=await cn.ExecuteScalarAsync<string>("SELECT TOP(1) ClienteCodigo FROM BK_MovimientoMatch WHERE MovimientoId=548 ORDER BY MatchId DESC;",transaction:tx)
                ?? throw new InvalidOperationException("Falta el cliente del movimiento de referencia del ensayo.");
            var fecha=original.Fecha.Date.AddDays(-7);
            var mov=await cn.ExecuteScalarAsync<long>("""
                INSERT BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,DescripcionOriginal,Importe,MonedaCodigo)
                VALUES(@import,'MercadoPago',@fecha,'ENSAYO_DUPLICADO',1119185,'ARS');
                DECLARE @id bigint=SCOPE_IDENTITY();
                INSERT BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza) VALUES(@id,'Cobranza',42);
                INSERT BK_MovimientoMatch(MovimientoId,TipoMatch,ClienteCodigo,Confianza,ResumenPropuesta)
                VALUES(@id,'Anticipo',@cliente,42,'Sin comprobantes pendientes');
                SELECT @id;
                """,new{import,fecha,cliente},tx);
            var asiento=await cn.ExecuteScalarAsync<int>("SELECT MAX([NUMERO ASIENTO])+1000 FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes;",original.Clave,tx);
            var key=new { original.Periodo,original.Mes,original.Asiento,original.TipoRegistro,original.Secuencia,asientoNuevo=asiento,numero=asiento.ToString("D8") };
            await cn.ExecuteAsync("""
                INSERT MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,
                    [DEBE-HABER],IMPORTE,MONEDA,PERIODO,CABIMPORTE,TIPO_REG,UNEGOCIO,USUARIO_LOGEADO,IDCAJAS,FechaSubdiario,EsResumen)
                SELECT CUENTA,1,MES_OPERATIVO,@asientoNuevo,FECHA,'ENSAYO_DUPLICADO','CB','9998',@numero,'X',
                    'H',1119185.81,'1',PERIODO,1119185.81,TIPO_REG,UNEGOCIO,USUARIO_LOGEADO,IDCAJAS,FechaSubdiario,0
                FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento
                    AND TIPO_REG=@TipoRegistro AND SECUENCIA=@Secuencia;
                """,key,tx);
            async Task<ConciliacionMovimiento> Leer()=>Assert.Single((await service.MovimientosCoreAsync(cn,new(){ImportacionId=import},default,tx)).Items);
            var row=await Leer();
            Assert.Equal(mov,row.Id);Assert.Equal("Anticipo",row.TipoMatch);Assert.Equal("Pendiente",row.Estado);
            Assert.Equal(original.Fecha.Date,row.FechaPosibleCobro?.Date);Assert.Equal(1119185.81m,row.ImportePosibleCobro);
            Assert.Contains("0,81",row.AvisoPosibleDuplicado);Assert.Contains("9998-",row.ReciboPosible);
            var repository=new ConciliacionRepository(cn,tx);
            Assert.True(await repository.HasRecentlyRegisteredPaymentAsync(cliente,1119185m,fecha));
            // La coincidencia requiere importe cercano, moneda local, crédito del cliente y fecha dentro del margen.
            foreach(var cambio in new[]{"IMPORTE=1119187","MONEDA='2'","[DEBE-HABER]='D'","FECHA=DATEADD(day,30,FECHA)"})
            {
                tx.Save("candidato");
                await cn.ExecuteAsync($"UPDATE MV_ASIENTOS SET {cambio} WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@asientoNuevo AND TIPO_REG=@TipoRegistro;",key,tx);
                Assert.Null((await Leer()).AvisoPosibleDuplicado);
                tx.Rollback("candidato");
            }
        }
        finally { tx.Rollback(); }
    }
}
