using System.Data;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    public Task<bool> ReconocerCobroAsync(long movimientoId,ConciliacionAsientoClave clave,decimal importeEsperado,
        string comprobanteEsperado,CancellationToken ct=default,string? observacion=null)
        => Ejecutar("ReconocerCobro",async(cn,token)=>
        {
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var result=await ReconocerCobroEnTransaccionAsync(cn,tx,movimientoId,clave,importeEsperado,comprobanteEsperado,token,observacion);
            tx.Commit();return result;
        },ct);

    internal async Task<bool> ReconocerCobroEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,long movimientoId,
        ConciliacionAsientoClave clave,decimal importeEsperado,string comprobanteEsperado,CancellationToken token=default,string? observacion=null)
    {
        var session=sessions.GetActiveSession()!.Id;
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
        var estado=await cn.QuerySingleOrDefaultAsync<string>(Q("SELECT EstadoRevision FROM dbo.BK_ExtractoMovimiento WITH(UPDLOCK,HOLDLOCK) WHERE MovimientoId=@movimientoId;",new{movimientoId}))
            ?? throw new InvalidOperationException("El movimiento ya no está disponible.");
        var p=new{movimientoId,clave.Periodo,clave.Mes,clave.Asiento,clave.TipoRegistro,clave.Secuencia,usuario=users.GetCurrentUserName()};
        using var r=await cn.QueryMultipleAsync(Q(CuentaCorrienteBaseSql+"""
            SELECT *,CONVERT(decimal(19,4),0) Saldo FROM #CtaBase WHERE Periodo=@Periodo AND Mes=@Mes AND Asiento=@Asiento
                AND TipoRegistro=@TipoRegistro AND Secuencia=@Secuencia AND ReciboCercano=1 AND CodigoMoneda='1'
                AND Haber>0 AND Debe=0 AND @importe>0
                AND EXISTS(SELECT 1 FROM dbo.BK_ExtractoMovimiento m WHERE m.MovimientoId=@movimientoId AND m.MonedaCodigo='ARS')
                AND (SELECT TOP(1) TipoMovimiento FROM dbo.BK_MovimientoClasificado WHERE MovimientoId=@movimientoId ORDER BY ClasificacionId DESC)='Cobranza';
            SELECT ISNULL(@cliente,'');
            """,p));
        var recibo=await r.ReadSingleOrDefaultAsync<ConciliacionCtaMovimiento>();var cliente=await r.ReadSingleAsync<string>();
        if(recibo is null||recibo.Haber!=importeEsperado||$"{recibo.Tc}|{recibo.Comprobante}"!=comprobanteEsperado)
            throw new InvalidOperationException("El recibo cambió o no coincide en cliente, importe, moneda y fecha. Actualizá el detalle.");
        if(observacion?.Length>300 || (recibo.Haber!=recibo.ImporteBanco && (observacion?.Trim().Length ?? 0)<3))
            throw new InvalidOperationException("Los importes del banco y del recibo son distintos. Indicá una observación de 3 a 300 caracteres para reconocer el cobro sin ajustar la contabilidad.");
        var existente=await cn.QuerySingleOrDefaultAsync<ConciliacionCtaMovimiento>(Q("""
            SELECT Periodo,Mes,Asiento,TipoRegistro,Secuencia FROM dbo.BK_CobroExistente WHERE MovimientoId=@movimientoId;
            """,p));
        if(existente is not null)
        {
            if(existente.Clave==clave&&estado=="Cobrado") { VerificarSesion(session);return false; }
            throw new InvalidOperationException("El movimiento ya tiene otro recibo reconocido.");
        }
        if(estado=="Cobrado") throw new InvalidOperationException("El movimiento ya tiene una cobranza registrada.");
        if(await cn.ExecuteScalarAsync<bool>(Q("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_CobroExistente WHERE CuentaCliente=@cliente
                AND Periodo=@Periodo AND Mes=@Mes AND Asiento=@Asiento AND TipoRegistro=@TipoRegistro AND Secuencia=@Secuencia)
                OR EXISTS(SELECT 1 FROM dbo.ALFACORE_COBRANZA_OPERACION WHERE @Tc='CB' AND Comprobante=@Comprobante
                    AND MovimientoId IS NOT NULL AND MovimientoId<>@movimientoId)
                THEN 1 ELSE 0 END);
            """,new{cliente,clave.Periodo,clave.Mes,clave.Asiento,clave.TipoRegistro,clave.Secuencia,recibo.Tc,recibo.Comprobante,movimientoId})))
            throw new InvalidOperationException("Este recibo ya fue reconocido para otro movimiento bancario.");
        await cn.ExecuteAsync(Q("""
            INSERT dbo.BK_CobroExistente(MovimientoId,CuentaCliente,Periodo,Mes,Asiento,TipoRegistro,Secuencia,Tc,Comprobante,Importe,Usuario,ImporteBanco,Observacion)
            VALUES(@movimientoId,@cliente,@Periodo,@Mes,@Asiento,@TipoRegistro,@Secuencia,@Tc,@Comprobante,@Haber,@usuario,@ImporteBanco,@observacion);
            UPDATE dbo.BK_ExtractoMovimiento SET EstadoRevision='Cobrado' WHERE MovimientoId=@movimientoId;
            INSERT dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
            VALUES(@movimientoId,'COBRO_EXISTENTE','OK',@mensaje,@usuario);
            """,new{movimientoId,cliente,clave.Periodo,clave.Mes,clave.Asiento,clave.TipoRegistro,clave.Secuencia,
            recibo.Tc,recibo.Comprobante,recibo.Haber,recibo.ImporteBanco,observacion=observacion?.Trim(),usuario=users.GetCurrentUserName(),
            mensaje=$"Cobro existente confirmado: {recibo.Tc} {recibo.Comprobante}. Banco {recibo.ImporteBanco}; recibo {recibo.Haber}. {observacion?.Trim()} No implica conciliación bancaria."}));
        VerificarSesion(session);token.ThrowIfCancellationRequested();return true;
    }
}
