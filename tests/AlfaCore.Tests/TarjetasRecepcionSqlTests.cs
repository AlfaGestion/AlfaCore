using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasRecepcionSqlTests
{
    [RecepcionSqlFact]
    public async Task MigracionIdempotenteYReclamoConLeaseSinCambiosPersistidos()
    {
        var conexion=Environment.GetEnvironmentVariable("TARJETAS_RECEPCION_TEST_SQL")!;
        Assert.Equal("PRUEBAS02122025",new SqlConnectionStringBuilder(conexion).InitialCatalog);
        await using var c=new SqlConnection(conexion);await c.OpenAsync();
        // Este ensayo crea TODA su cola bajo rollback. Nunca toma trabajos existentes.
        Assert.Equal(0,await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID(N'dbo.TJ_Recepcion')"));
        using var tx=c.BeginTransaction();
        try
        {
            var raiz=new DirectoryInfo(AppContext.BaseDirectory);while(raiz is not null&&!File.Exists(Path.Combine(raiz.FullName,"AlfaCore.sln")))raiz=raiz.Parent;
            Assert.NotNull(raiz);
            foreach(var nombre in new[]{"2026-10-31-002__caja_tarjetas_analisis_persistente.sql","2026-10-31-003__interfaces_tarjetas_recepcion.sql","2026-10-31-003__interfaces_tarjetas_recepcion.sql"})
                await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(raiz!.FullName,"src/AlfaCore/App_Data/updates",nombre)),transaction:tx);
            await c.ExecuteAsync("SET NOCOUNT OFF;",transaction:tx);
            var documento=await c.ExecuteScalarAsync<long>("""
                INSERT dbo.INT_COMPROBANTE_RECIBIDO(UsuarioAlta,PcAlta,IdEstado,IdTipoDocumento,Observacion,CantidadAdjuntos,RutaBase,FechaHoraEstado,Eliminado)
                OUTPUT INSERTED.IdComprobanteRecibido
                SELECT N'PRUEBA_COLA',N'PRUEBA',
                    (SELECT TOP(1) IdEstado FROM dbo.INT_ESTADO ORDER BY IdEstado),IdTipoDocumento,N'Ensayo con rollback',0,N'',GETDATE(),0
                FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo=N'LIQUIDACION_TARJETAS';
                """,transaction:tx);
            await c.ExecuteAsync("INSERT dbo.TJ_Recepcion(DocumentoId,Usuario,Sistema,Nombre,Original,Hash) VALUES(@documento,'PRUEBA_COLA','CN000PR','prueba.pdf',0x255044462D,'PRUEBA')",new{documento},tx);
            var lease=Guid.NewGuid();
            var primero=await c.QuerySingleAsync<TarjetasRecepcionTrabajo>(TarjetasRecepcionService.ReclamarSql,new{lease,duracion=25},tx);
            Assert.Equal(1,primero.Intentos);Assert.Equal(lease,primero.Lease);Assert.Equal("LEYENDO",primero.Estado);
            Assert.Null(await c.QuerySingleOrDefaultAsync<TarjetasRecepcionTrabajo>(TarjetasRecepcionService.ReclamarSql,new{lease=Guid.NewGuid(),duracion=25},tx));
            Assert.Equal(0,await c.ExecuteAsync("UPDATE dbo.TJ_Recepcion SET Estado='LISTO' WHERE Id=@Id AND Lease=@lease",new{primero.Id,lease=Guid.NewGuid()},tx));
            await c.ExecuteAsync("UPDATE dbo.TJ_Recepcion SET HastaUtc=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@Id",primero,tx);
            var segundo=await c.QuerySingleAsync<TarjetasRecepcionTrabajo>(TarjetasRecepcionService.ReclamarSql,new{lease=Guid.NewGuid(),duracion=25},tx);
            Assert.Equal(2,segundo.Intentos);Assert.NotEqual(primero.Lease,segundo.Lease);Assert.Equal(primero.Original,segundo.Original);
            await c.ExecuteAsync("UPDATE dbo.TJ_Recepcion SET Estado='CUENTA',Lease=NULL WHERE Id=@Id",segundo,tx);
            Assert.Null(await c.QuerySingleOrDefaultAsync<TarjetasRecepcionTrabajo>(TarjetasRecepcionService.ReclamarSql,new{lease=Guid.NewGuid(),duracion=25},tx));
            await c.ExecuteAsync("UPDATE dbo.TJ_Recepcion SET Estado='PENDIENTE',ProximoUtc=SYSUTCDATETIME() WHERE Id=@Id",segundo,tx);
            var tercero=await c.QuerySingleAsync<TarjetasRecepcionTrabajo>(TarjetasRecepcionService.ReclamarSql,new{lease=Guid.NewGuid(),duracion=25},tx);
            Assert.Equal(3,tercero.Intentos);
            await c.ExecuteAsync("UPDATE dbo.TJ_Recepcion SET HastaUtc=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@Id",tercero,tx);
            Assert.Null(await c.QuerySingleOrDefaultAsync<TarjetasRecepcionTrabajo>(TarjetasRecepcionService.ReclamarSql,new{lease=Guid.NewGuid(),duracion=25},tx));
        }
        finally{if(tx.Connection is not null)await tx.RollbackAsync();}
        Assert.Equal(0,await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID(N'dbo.TJ_Recepcion')"));
    }
}
public sealed class RecepcionSqlFactAttribute:FactAttribute
{
    public RecepcionSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_RECEPCION_TEST_SQL")))Skip="Requiere PRUEBAS02122025 autorizada sin cola TJ_Recepcion; DDL/datos auxiliares bajo rollback. No toca asientos.";}
}
