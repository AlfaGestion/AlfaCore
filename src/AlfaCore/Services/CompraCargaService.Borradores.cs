using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    public Task<IReadOnlyList<CompraBorradorGuardado>> BorradoresAsync(int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<CompraBorradorGuardado>>(baseEsperada,"Borradores",async(c,t)=>
        {
            if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID('dbo.ALFACORE_COMPRA_BORRADOR');",null,t))==0)return [];
            return (await c.QueryAsync<CompraBorradorGuardado>(Cmd("""
                SELECT TOP(100) Id,Version,Nombre,Json,Modificado FROM dbo.ALFACORE_COMPRA_BORRADOR
                WHERE Usuario=@usuario AND Sistema=@sistema ORDER BY Modificado DESC,Id;
                """,new{usuario=usuarios.GetCurrentUserName(),sistema=usuarios.CurrentUser?.SystemCode??""},t))).AsList();
        },ct);

    public Task<int> GuardarBorradorAsync(Guid id,int version,string nombre,string json,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"GuardarBorrador",async(c,t)=>
        {
            using var tx=c.BeginTransaction(System.Data.IsolationLevel.Serializable);
            var actor=usuarios.GetCurrentUserName();var sistema=usuarios.CurrentUser?.SystemCode??"";var sesion=sesiones.GetActiveSession()?.Id;
            var siguiente=await GuardarBorradorEnTransaccionAsync(c,tx,id,version,nombre,json,t);
            if(sesiones.GetActiveSession()?.Id!=sesion||usuarios.GetCurrentUserName()!=actor||(usuarios.CurrentUser?.SystemCode??"")!=sistema)throw new OperationCanceledException("La sesión cambió.");
            t.ThrowIfCancellationRequested();tx.Commit();return siguiente;
        },ct);

    internal async Task<int> GuardarBorradorEnTransaccionAsync(SqlConnection c,SqlTransaction tx,Guid id,int version,string nombre,string json,CancellationToken ct)
    {
        if(!TenantDataAccessGuard.IsActiveSessionAuthorized(sesiones,usuarios)||!await permisos.HasAccessAsync(MenuKey,ct))throw new InvalidOperationException("La sesión no está autorizada para guardar borradores.");
        if(id==Guid.Empty||version<0||nombre.Length>160||json.Length>500000)throw new InvalidOperationException("Revisá los datos del borrador.");
        using var documento=JsonDocument.Parse(json);
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID('dbo.ALFACORE_COMPRA_BORRADOR');",null,ct,tx))==0)
            throw new InvalidOperationException("Aplicá las actualizaciones de Compras para guardar borradores.");
        var actor=usuarios.GetCurrentUserName();var sistema=usuarios.CurrentUser?.SystemCode??"";
        var actual=await c.QuerySingleOrDefaultAsync<CompraBorradorGuardado>(Cmd("""
            SELECT Id,Version,Nombre,Json,Modificado FROM dbo.ALFACORE_COMPRA_BORRADOR WITH(UPDLOCK,HOLDLOCK)
            WHERE Id=@id AND Usuario=@actor AND Sistema=@sistema;
            """,new{id,actor,sistema},ct,tx));
        if((actual?.Version??0)!=version)throw new InvalidOperationException("Otra ventana modificó el borrador. Recuperalo antes de reemplazarlo.");
        if(actual is null)
            await c.ExecuteAsync(Cmd("INSERT dbo.ALFACORE_COMPRA_BORRADOR(Id,Usuario,Sistema,Version,Nombre,Json) VALUES(@id,@actor,@sistema,1,@nombre,@json);",new{id,actor,sistema,nombre,json},ct,tx));
        else
            await c.ExecuteAsync(Cmd("UPDATE dbo.ALFACORE_COMPRA_BORRADOR SET Version=Version+1,Nombre=@nombre,Json=@json,Modificado=GETDATE() WHERE Id=@id AND Usuario=@actor AND Sistema=@sistema AND Version=@version;",new{id,actor,sistema,nombre,json,version},ct,tx));
        ct.ThrowIfCancellationRequested();return version+1;
    }
}
