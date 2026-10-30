using System.Data;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasAnularImportacionTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }

    [TarjetasVariasSqlFact]
    public async Task AnulaSoloSinVinculosActivosConservaTestigoHistorialYPermiteReimportar()
    {
        var connection=Environment.GetEnvironmentVariable("TARJETAS_VARIAS_TEST_SQL")!;
        Assert.Contains(new SqlConnectionStringBuilder(connection).InitialCatalog,new[]{"ELBOLSON2011_PRUEBAS","PRUEBAS02122025"});
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"src/AlfaCore/App_Data/updates/2026-10-30-004__caja_tarjetas_anular_importacion.sql")))root=root.Parent;
        Assert.NotNull(root);
        var script=await File.ReadAllTextAsync(Path.Combine(root!.FullName,"src/AlfaCore/App_Data/updates/2026-10-30-004__caja_tarjetas_anular_importacion.sql"));
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
        var sesion=new SessionDto();var auditorias=new List<AuditWriteRequest>();var fallarAuditoria=false;
        var eventos=Proxy<IAppEventService>((m,args)=>
        {
            Assert.Equal("WriteAuditAsync",m.Name);Assert.Same(cn,args![1]);Assert.Same(tx,args[2]);
            if(fallarAuditoria)return Task.FromException<Guid>(new InvalidOperationException("Auditoría no disponible"));
            auditorias.Add((AuditWriteRequest)args[0]!);return Task.FromResult(Guid.NewGuid());
        });
        var service=new ConciliacionTarjetasService(
            Proxy<ISessionService>((m,_)=>m.Name=="GetActiveSession"?sesion:null),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"ENSAYO",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),eventos,null!);
        var tag=Guid.NewGuid().ToString("N");var hash=tag.PadRight(64,'0');var huella=tag.PadRight(64,'1');
        async Task<long> Crear()
        {
            var id=await cn.ExecuteScalarAsync<long>("""
                INSERT dbo.TJ_Importacion(Nombre,Original,Hash,Banco,Comercio,Tarjeta,Cuenta,Sucursal,Desde,Hasta,Bruto,Neto,DetalleCompleto,Avisos,Usuario)
                OUTPUT INSERTED.Id VALUES('ENSAYO_ANULAR',0x255044462D,@hash,'Ensayo',@tag,'VISA','DEMO-ANULAR','','20260401','20260401',100,90,1,'','ENSAYO');
                """,new{hash,tag},tx);
            await cn.ExecuteAsync("""
                INSERT dbo.TJ_Detalle(ImportacionId,Fecha,Liquidacion,Lote,Cupon,Terminal,Nivel,Cantidad,Importe,Huella)
                VALUES(@id,'20260401','1','1','','','Lote',1,100,@huella);
                """,new{id,huella},tx);return id;
        }
        try
        {
            // Migración real e idempotencia, incluidas en la transacción exterior que se revierte.
            await cn.ExecuteAsync(script,transaction:tx);await cn.ExecuteAsync(script,transaction:tx);
            var id=await Crear();
            var detalle=await cn.ExecuteScalarAsync<long>("SELECT Id FROM dbo.TJ_Detalle WHERE ImportacionId=@id",new{id},tx);
            var vinculo=await cn.ExecuteScalarAsync<long>("INSERT dbo.TJ_Vinculo(ImportacionId,Activo,ImporteResumen,ImporteAlfa,Motivo,Usuario) OUTPUT INSERTED.Id VALUES(@id,1,100,100,'Ensayo','ENSAYO')",new{id},tx);
            await cn.ExecuteAsync("""
                INSERT dbo.TJ_VinculoDetalle(VinculoId,DetalleId,Activo) VALUES(@vinculo,@detalle,1);
                INSERT dbo.TJ_VinculoAlfa(VinculoId,Cuenta,Clave,Huella,Importe,Activo) VALUES(@vinculo,'DEMO-ANULAR',@tag,@huella,100,1);
                """,new{vinculo,detalle,tag,huella},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnularEnTransaccionAsync(cn,tx,id,"DEMO-ANULAR","",default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnularEnTransaccionAsync(cn,tx,id,"OTRA","Error de archivo",default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnularEnTransaccionAsync(cn,tx,id,"DEMO-ANULAR","Aún conciliado",default));
            await cn.ExecuteAsync("UPDATE dbo.TJ_Vinculo SET Activo=0 WHERE Id=@vinculo",new{vinculo},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnularEnTransaccionAsync(cn,tx,id,"DEMO-ANULAR","Miembros aún activos",default));
            await cn.ExecuteAsync("UPDATE dbo.TJ_VinculoDetalle SET Activo=0 WHERE VinculoId=@vinculo",new{vinculo},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnularEnTransaccionAsync(cn,tx,id,"DEMO-ANULAR","Alfa aún activo",default));
            await cn.ExecuteAsync("UPDATE dbo.TJ_VinculoAlfa SET Activo=0 WHERE VinculoId=@vinculo",new{vinculo},tx);
            fallarAuditoria=true;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnularEnTransaccionAsync(cn,tx,id,"DEMO-ANULAR","Error de auditoría",default));
            Assert.False(await cn.ExecuteScalarAsync<bool>("SELECT Anulada FROM dbo.TJ_Importacion WHERE Id=@id",new{id},tx));
            fallarAuditoria=false;
            await service.AnularEnTransaccionAsync(cn,tx,id,"DEMO-ANULAR","Me equivoqué de archivo",default);
            var fila=await cn.QuerySingleAsync<(bool Anulada,string Usuario,string Motivo,byte[] Original)>("SELECT Anulada,UsuarioAnulacion Usuario,MotivoAnulacion Motivo,Original FROM dbo.TJ_Importacion WHERE Id=@id",new{id},tx);
            Assert.True(fila.Anulada);Assert.Equal("ENSAYO",fila.Usuario);Assert.Equal("Me equivoqué de archivo",fila.Motivo);
            Assert.Equal(new byte[]{0x25,0x50,0x44,0x46,0x2D},fila.Original);
            Assert.False(await cn.ExecuteScalarAsync<bool>("SELECT ImportacionActiva FROM dbo.TJ_Detalle WHERE Id=@detalle",new{detalle},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Vinculo WHERE Id=@vinculo",new{vinculo},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_VinculoDetalle WHERE VinculoId=@vinculo",new{vinculo},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_VinculoAlfa WHERE VinculoId=@vinculo",new{vinculo},tx));
            Assert.Single(auditorias);Assert.Equal("Update",auditorias[0].Operation);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AnularEnTransaccionAsync(cn,tx,id,"DEMO-ANULAR","Repetida",default));
            // Mismo PDF, cuenta y huella: se permite una nueva importación activa.
            var nuevo=await Crear();Assert.NotEqual(id,nuevo);
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Importacion WHERE Hash=@hash AND Cuenta='DEMO-ANULAR' AND Anulada=0",new{hash},tx));
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Detalle WHERE Huella=@huella AND ImportacionActiva=1",new{huella},tx));
        }
        finally{tx.Rollback();}
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Importacion WHERE Comercio=@tag",new{tag}));
    }
}
