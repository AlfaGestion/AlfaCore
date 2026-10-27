using System.Data;
using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    public Task<bool> EliminarAsync(long id,string cuentaAnterior,string motivo,CancellationToken ct=default)
        =>Ejecutar("EliminarImportacion",async(c,t)=>
        {
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            await EliminarEnTransaccionAsync(c,tx,id,cuentaAnterior,motivo,t);
            tx.Commit();return true;
        },ct);

    internal async Task EliminarEnTransaccionAsync(SqlConnection c,SqlTransaction tx,long id,string cuentaAnterior,string motivo,CancellationToken ct)
    {
        var session=sessions.GetActiveSession()!.Id;motivo=motivo.Trim();
        if(motivo.Length is <3 or >200)throw new InvalidOperationException("Escribí un motivo de entre 3 y 200 caracteres para eliminar la importación.");
        var origen=await c.QuerySingleOrDefaultAsync<TarjetasImportacion>(Cmd("SELECT Id,Nombre,Cuenta FROM dbo.TJ_Importacion WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id",new{id},ct,tx))
            ??throw new InvalidOperationException("La importación ya no está disponible. Actualizá la lista.");
        if(origen.Cuenta.Trim()!=cuentaAnterior.Trim())throw new InvalidOperationException("La cuenta cambió en otra sesión. Actualizá la importación antes de eliminarla.");
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.TJ_Vinculo WITH(UPDLOCK,HOLDLOCK) WHERE ImportacionId=@id",new{id},ct,tx))>0
            ||await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.TJ_VinculoDetalle v WITH(UPDLOCK,HOLDLOCK) JOIN dbo.TJ_Detalle d ON d.Id=v.DetalleId WHERE d.ImportacionId=@id",new{id},ct,tx))>0)
            throw new InvalidOperationException("La importación tiene historial de conciliación y no puede eliminarse, aunque los vínculos estén anulados. Usá Ver conciliados para revisar o deshacer los vínculos.");
        await events.WriteAuditAsync(new AuditWriteRequest
        {
            Module="ConciliacionTarjetas",EntityType="TarjetasImportacion",RecordId=id.ToString(CultureInfo.InvariantCulture),Operation="Delete",
            Message="Importación de tarjetas eliminada: "+motivo,
            Metadata=new Dictionary<string,string?>{{"Archivo",origen.Nombre},{"Cuenta",origen.Cuenta},{"Motivo",motivo}}
        },c,tx,ct);
        await c.ExecuteAsync(Cmd("DELETE FROM dbo.TJ_Detalle WHERE ImportacionId=@id; DELETE FROM dbo.TJ_Importacion WHERE Id=@id;",new{id},ct,tx));
        AntesDeGrabar(session,ct);
    }
}
