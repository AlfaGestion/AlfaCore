using System.Data;
using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    private static async Task<bool> TieneAnulacionAsync(SqlConnection c,CancellationToken ct,SqlTransaction? tx=null)
        =>await c.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN COL_LENGTH('dbo.TJ_Importacion','Anulada') IS NOT NULL AND COL_LENGTH('dbo.TJ_Detalle','ImportacionActiva') IS NOT NULL THEN 1 ELSE 0 END",null,ct,tx))==1;

    public Task<bool> AnularAsync(long id,string cuentaAnterior,string motivo,CancellationToken ct=default)
        =>Ejecutar("AnularImportacion",async(c,t)=>
        {
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            await AnularEnTransaccionAsync(c,tx,id,cuentaAnterior,motivo,t);
            tx.Commit();return true;
        },ct);

    internal async Task AnularEnTransaccionAsync(SqlConnection c,SqlTransaction tx,long id,string cuentaAnterior,string motivo,CancellationToken ct)
    {
        var session=sessions.GetActiveSession()!.Id;motivo=motivo.Trim();
        if(motivo.Length is <3 or >200)throw new InvalidOperationException("Escribí un motivo de entre 3 y 200 caracteres para anular la importación.");
        if(!await TieneAnulacionAsync(c,ct,tx))throw new InvalidOperationException("Aplicá la actualización 2026-10-30-004 desde Actualizaciones antes de anular importaciones de tarjetas.");
        var origen=await c.QuerySingleOrDefaultAsync<TarjetasImportacion>(Cmd("SELECT Id,Nombre,Cuenta,Anulada FROM dbo.TJ_Importacion WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id",new{id},ct,tx))
            ??throw new InvalidOperationException("La importación ya no está disponible. Actualizá la lista.");
        if(origen.Anulada)throw new InvalidOperationException("La importación ya está anulada. Actualizá la lista.");
        if(origen.Cuenta.Trim()!=cuentaAnterior.Trim())throw new InvalidOperationException("La cuenta cambió en otra sesión. Actualizá la importación antes de anularla.");
        // También se verifican asociaciones activas inconsistentes; nunca se liberan silenciosamente.
        if(await c.ExecuteScalarAsync<int>(Cmd("""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TJ_Vinculo WITH(UPDLOCK,HOLDLOCK) WHERE ImportacionId=@id AND Activo=1)
              OR EXISTS(SELECT 1 FROM dbo.TJ_VinculoDetalle vd WITH(UPDLOCK,HOLDLOCK)
                 LEFT JOIN dbo.TJ_Vinculo v WITH(UPDLOCK,HOLDLOCK) ON v.Id=vd.VinculoId
                 LEFT JOIN dbo.TJ_Detalle d WITH(UPDLOCK,HOLDLOCK) ON d.Id=vd.DetalleId
                 WHERE vd.Activo=1 AND (d.ImportacionId=@id OR v.ImportacionId=@id))
              OR EXISTS(SELECT 1 FROM dbo.TJ_VinculoAlfa va WITH(UPDLOCK,HOLDLOCK)
                 JOIN dbo.TJ_Vinculo v WITH(UPDLOCK,HOLDLOCK) ON v.Id=va.VinculoId
                 WHERE va.Activo=1 AND v.ImportacionId=@id)
              THEN 1 ELSE 0 END
            """,new{id},ct,tx))!=0)
            throw new InvalidOperationException("Todavía hay conciliaciones activas en esta importación. Usá Deshacer varias antes de anularla.");
        await events.WriteAuditAsync(new AuditWriteRequest
        {
            Module="ConciliacionTarjetas",EntityType="TarjetasImportacion",RecordId=id.ToString(CultureInfo.InvariantCulture),Operation="Update",
            Message="Importación de tarjetas anulada: "+motivo,
            Metadata=new Dictionary<string,string?>{{"Archivo",origen.Nombre},{"Cuenta",origen.Cuenta},{"Motivo",motivo}}
        },c,tx,ct);
        await c.ExecuteAsync(Cmd("""
            UPDATE dbo.TJ_Importacion SET Anulada=1,UsuarioAnulacion=@usuario,FechaAnulacion=SYSUTCDATETIME(),MotivoAnulacion=@motivo WHERE Id=@id;
            UPDATE dbo.TJ_Detalle SET ImportacionActiva=0 WHERE ImportacionId=@id;
            """,new{id,usuario=users.GetCurrentUserName(),motivo},ct,tx));
        AntesDeGrabar(session,ct);
    }
}
