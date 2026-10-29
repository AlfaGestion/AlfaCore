using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasDesconciliarSqlTests
{
    [TarjetasVariasSqlFact]
    public async Task AutorizaVinculoCompletoSinFiltrosYConservaUnidadesEIdentidad()
    {
        var connection=Environment.GetEnvironmentVariable("TARJETAS_VARIAS_TEST_SQL")!;
        Assert.Contains(new SqlConnectionStringBuilder(connection).InitialCatalog,new[]{"ELBOLSON2011_PRUEBAS","PRUEBAS02122025"});
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        // Exclusivamente tablas temporales: no se consulta ni escribe contabilidad real.
        await cn.ExecuteAsync("""
            CREATE TABLE #Usuarios(NOMBRE nvarchar(50),AdminCentral bit,UNEGOCIO nvarchar(4));
            INSERT #Usuarios VALUES('ENSAYO',0,'1');
            CREATE TABLE #Cabeceras(Id bigint,ImportacionId bigint,Activo bit);
            INSERT #Cabeceras VALUES(1,5,1),(2,5,1),(3,5,1),(4,5,1);
            CREATE TABLE #Vinculos(VinculoId bigint,Cuenta nvarchar(15),Clave nvarchar(200),Huella varchar(64),Activo bit);
            CREATE TABLE #Movimientos(PERIODO nvarchar(6),MES_OPERATIVO tinyint,[NUMERO ASIENTO] int,TIPO_REG nvarchar(4),
                SECUENCIA int,FECHA date,IMPORTE decimal(19,2),CUENTA nvarchar(15),TC nvarchar(4),SUCURSAL nvarchar(4),
                NUMERO nvarchar(8),LETRA nvarchar(1),UNEGOCIO nvarchar(4),TJ_NroCupon nvarchar(30),
                TJ_Presentacion int,TJ_Liquidacion int,DETALLE nvarchar(100));
            INSERT #Movimientos VALUES
                ('18',3,1,'',1,'20250101',100,'TJ-ENSAYO','CB','0001','00000001','X','1','1',0,0,'Ensayo'),
                ('18',3,2,'',1,'20280101',200,'TJ-ENSAYO','CB','9999','00000002','X','2','2',0,0,'Ensayo'),
                ('18',3,4,'',1,'20250101',400,'TJ-ENSAYO','CB','0001','00000004','X','1','4',0,0,'Ensayo'),
                ('18',3,4,'',1,'20250101',400,'TJ-ENSAYO','CB','0001','00000004','X','2','4',0,0,'Ensayo');
            """);
        await cn.ExecuteAsync($"""
            INSERT #Vinculos SELECT 1,a.CUENTA,{ConciliacionTarjetasService.ClaveMovimientoSql},{ConciliacionTarjetasService.HuellaMovimientoSql},1
                FROM #Movimientos a WHERE a.[NUMERO ASIENTO] IN(1,2);
            INSERT #Vinculos SELECT 2,a.CUENTA,{ConciliacionTarjetasService.ClaveAnteriorMovimientoSql},{ConciliacionTarjetasService.HuellaMovimientoSql},1
                FROM #Movimientos a WHERE a.[NUMERO ASIENTO]=1;
            INSERT #Vinculos VALUES(3,'TJ-ENSAYO','INEXISTENTE','',1);
            INSERT #Vinculos SELECT TOP(1) 4,a.CUENTA,{ConciliacionTarjetasService.ClaveMovimientoSql},{ConciliacionTarjetasService.HuellaMovimientoSql},1
                FROM #Movimientos a WHERE a.[NUMERO ASIENTO]=4;
            """);
        var sql=ConciliacionTarjetasService.AutorizarDesconciliacionSql.Replace("dbo.MV_ASIENTOS","#Movimientos")
            .Replace("dbo.TA_USUARIOS","#Usuarios").Replace("dbo.TJ_VinculoAlfa","#Vinculos").Replace("dbo.TJ_Vinculo","#Cabeceras");
        async Task<Dictionary<long,TarjetasVinculoDeshacer>> Consultar()=>(await cn.QueryAsync<TarjetasVinculoDeshacer>(sql,
            new{importacion=5,cuenta="TJ-ENSAYO",usuario="ENSAYO"})).ToDictionary(v=>v.Id);
        var grupos=await Consultar();
        Assert.Equal(2,grupos[1].Movimientos);Assert.Equal(1,grupos[1].Autorizados);
        Assert.Equal(1,grupos[2].Autorizados); // clave antigua con huella exacta
        Assert.Equal(0,grupos[3].Autorizados); // movimiento desaparecido
        Assert.Equal(0,grupos[4].Autorizados); // clave ambigua entre unidades
        await cn.ExecuteAsync("UPDATE #Usuarios SET AdminCentral=1 WHERE NOMBRE='ENSAYO'");
        grupos=await Consultar();Assert.Equal(2,grupos[1].Autorizados);Assert.Equal(0,grupos[4].Autorizados);
        await cn.ExecuteAsync("UPDATE #Vinculos SET Huella='OBSOLETA' WHERE VinculoId=2");
        Assert.Equal(0,(await Consultar())[2].Autorizados);
    }
}
