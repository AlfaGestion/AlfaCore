using System.Data;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasEliminarImportacionTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }

    [CambioCuentaSqlFact]
    public async Task EliminaSoloElLoteSinHistorialConAuditoriaTransaccionalYPermiteReimportarlo()
    {
        var connection=Environment.GetEnvironmentVariable("TARJETAS_CUENTA_TEST_SQL")!;
        Assert.Equal("ELBOLSON2011_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
        var sesion=new SessionDto();AuditWriteRequest? auditoria=null;var fallarAuditoria=false;
        var eventos=Proxy<IAppEventService>((m,args)=>
        {
            Assert.Equal("WriteAuditAsync",m.Name);Assert.Same(cn,args![1]);Assert.Same(tx,args[2]);
            if(fallarAuditoria)return Task.FromException<Guid>(new InvalidOperationException("Auditoría no disponible"));
            auditoria=(AuditWriteRequest)args[0]!;return Task.FromResult(Guid.NewGuid());
        });
        var service=new ConciliacionTarjetasService(
            Proxy<ISessionService>((m,_)=>m.Name=="GetActiveSession"?sesion:null),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"SISTEMAS",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),eventos,null!);
        var tag=Guid.NewGuid().ToString("N");var hash=tag.PadRight(64,'0');
        async Task<long> Crear(string h)
        {
            var id=await cn.ExecuteScalarAsync<long>("""
                INSERT dbo.TJ_Importacion(Nombre,Original,Hash,Banco,Comercio,Tarjeta,Cuenta,Sucursal,Desde,Hasta,Bruto,Neto,DetalleCompleto,Avisos,Usuario)
                OUTPUT INSERTED.Id VALUES('ENSAYO_ELIMINAR',0x255044462D,@h,'Ensayo',@tag,'VISA','DEMO-ELIMINAR','','20260401','20260401',100,90,1,'','SISTEMAS');
                """,new{h,tag},tx);
            await cn.ExecuteAsync("""
                INSERT dbo.TJ_Detalle(ImportacionId,Fecha,Liquidacion,Lote,Cupon,Terminal,Nivel,Cantidad,Importe,Huella)
                VALUES(@id,'20260401','1','1','','','Lote',1,100,@huella);
                """,new{id,huella=Guid.NewGuid().ToString("N").PadRight(64,'0')},tx);return id;
        }
        try
        {
            var id=await Crear(hash);var otro=await Crear(tag.PadRight(64,'1'));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EliminarEnTransaccionAsync(cn,tx,id,"DEMO-ELIMINAR","",default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EliminarEnTransaccionAsync(cn,tx,id,"OTRA","Me equivoqué de archivo",default));
            var vinculo=await cn.ExecuteScalarAsync<long>("INSERT dbo.TJ_Vinculo(ImportacionId,Activo,ImporteResumen,ImporteAlfa,Motivo,Usuario) OUTPUT INSERTED.Id VALUES(@id,1,100,100,'Ensayo','SISTEMAS')",new{id},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EliminarEnTransaccionAsync(cn,tx,id,"DEMO-ELIMINAR","Con vínculo activo",default));
            await cn.ExecuteAsync("UPDATE dbo.TJ_Vinculo SET Activo=0 WHERE Id=@vinculo",new{vinculo},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EliminarEnTransaccionAsync(cn,tx,id,"DEMO-ELIMINAR","Con vínculo anulado",default));
            Assert.Null(auditoria);
            await cn.ExecuteAsync("DELETE dbo.TJ_Vinculo WHERE Id=@vinculo",new{vinculo},tx);
            fallarAuditoria=true;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EliminarEnTransaccionAsync(cn,tx,id,"DEMO-ELIMINAR","Fallo de auditoría",default));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Detalle WHERE ImportacionId=@id",new{id},tx));
            fallarAuditoria=false;
            await service.EliminarEnTransaccionAsync(cn,tx,id,"DEMO-ELIMINAR","Me equivoqué de archivo",default);
            Assert.Equal("Delete",auditoria!.Operation);Assert.Equal(id.ToString(),auditoria.RecordId);Assert.Contains("equivoqué",auditoria.Message);
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Importacion WHERE Id=@id",new{id},tx));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Detalle WHERE ImportacionId=@id",new{id},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Importacion WHERE Id=@otro",new{otro},tx));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EliminarEnTransaccionAsync(cn,tx,id,"DEMO-ELIMINAR","Solicitud repetida",default));
            Assert.True(await Crear(hash)>id);
        }
        finally{tx.Rollback();}
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Importacion WHERE Comercio=@tag",new{tag}));
    }
}
