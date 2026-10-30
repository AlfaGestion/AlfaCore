using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
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
            INSERT #Cabeceras VALUES(1,5,1),(2,5,1),(3,5,1),(4,5,1),(5,5,1);
            CREATE TABLE #Vinculos(VinculoId bigint,Cuenta nvarchar(15),Clave nvarchar(200),Huella varchar(64),Activo bit);
            CREATE TABLE #Movimientos(PERIODO nvarchar(6),MES_OPERATIVO tinyint,[NUMERO ASIENTO] int,TIPO_REG nvarchar(4),
                SECUENCIA int,FECHA date,IMPORTE decimal(19,2),CUENTA nvarchar(15),TC nvarchar(4),SUCURSAL nvarchar(4),
                NUMERO nvarchar(8),LETRA nvarchar(1),UNEGOCIO nvarchar(4),TJ_NroCupon nvarchar(30),
                TJ_Presentacion int,TJ_Liquidacion int,DETALLE nvarchar(100));
            INSERT #Movimientos VALUES
                ('18',3,1,'',1,'20250101',100,'TJ-ENSAYO','CB','0001','00000001','X','1','1',0,0,'Ensayo'),
                ('18',3,2,'',1,'20280101',200,'TJ-ENSAYO','CB','9999','00000002','X','2','2',0,0,'Ensayo'),
                ('18',3,4,'',1,'20250101',400,'TJ-ENSAYO','CB','0001','00000004','X','1','4',0,0,'Ensayo'),
                ('18',3,4,'',1,'20250101',400,'TJ-ENSAYO','CB','0001','00000004','X','2','4',0,0,'Ensayo'),
                ('18',3,5,'',1,'20250101',500,'  TJ-ENSAYO','CB','0001','00000005','X','1','5',0,0,'Ensayo');
            ;WITH numeros AS (
                SELECT TOP(100000) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b
            )
            INSERT #Movimientos
                SELECT '18',3,CONVERT(int,n+10000),'',1,'20200101',10,'TJ-ENSAYO','CB','0001','99999999','X','1','1',0,0,'No vinculado'
                FROM numeros;
            CREATE INDEX IX_Identidad ON #Movimientos(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],TC,SUCURSAL,NUMERO,LETRA,PERIODO,TIPO_REG);
            """);
        await cn.ExecuteAsync($"""
            INSERT #Vinculos SELECT 1,a.CUENTA,{ConciliacionTarjetasService.ClaveMovimientoSql},{ConciliacionTarjetasService.HuellaMovimientoSql},1
                FROM #Movimientos a WHERE a.[NUMERO ASIENTO] IN(1,2);
            INSERT #Vinculos SELECT 2,a.CUENTA,{ConciliacionTarjetasService.ClaveAnteriorMovimientoSql},{ConciliacionTarjetasService.HuellaMovimientoSql},1
                FROM #Movimientos a WHERE a.[NUMERO ASIENTO]=1;
            INSERT #Vinculos VALUES(3,'TJ-ENSAYO','INEXISTENTE','',1);
            INSERT #Vinculos SELECT TOP(1) 4,a.CUENTA,{ConciliacionTarjetasService.ClaveMovimientoSql},{ConciliacionTarjetasService.HuellaMovimientoSql},1
                FROM #Movimientos a WHERE a.[NUMERO ASIENTO]=4;
            INSERT #Vinculos SELECT 5,LTRIM(RTRIM(a.CUENTA)),{ConciliacionTarjetasService.ClaveMovimientoSql},{ConciliacionTarjetasService.HuellaMovimientoSql},1
                FROM #Movimientos a WHERE a.[NUMERO ASIENTO]=5;
            """);
        var sql=ConciliacionTarjetasService.AutorizarDesconciliacionSql.Replace("dbo.MV_ASIENTOS","#Movimientos")
            .Replace("dbo.TA_USUARIOS","#Usuarios").Replace("dbo.TJ_VinculoAlfa","#Vinculos").Replace("dbo.TJ_Vinculo","#Cabeceras");
        async Task<Dictionary<long,TarjetasVinculoDeshacer>> Consultar()
        {
            var claves=await cn.QueryAsync<ConciliacionTarjetasService.ClaveDeshacer>("SELECT VinculoId,Cuenta,Clave FROM #Vinculos;");
            return (await cn.QueryAsync<TarjetasVinculoDeshacer>(sql,
                new{importacion=5,cuenta="TJ-ENSAYO",usuario="ENSAYO",clavesXml=ConciliacionTarjetasService.ClavesDeshacerXml(claves)})).ToDictionary(v=>v.Id);
        }
        var lecturas=new List<int>();
        cn.InfoMessage+=(_,e)=>
        {
            foreach(SqlError mensaje in e.Errors)
            {
                if(!mensaje.Message.Contains("#Movimientos",StringComparison.OrdinalIgnoreCase))continue;
                var coincidencia=Regex.Match(mensaje.Message,@"(?:logical reads|lecturas lógicas)\s+(\d+)",RegexOptions.IgnoreCase);
                if(coincidencia.Success)lecturas.Add(int.Parse(coincidencia.Groups[1].Value));
            }
        };
        await cn.ExecuteAsync("SET STATISTICS IO ON;");
        var grupos=await Consultar();
        await cn.ExecuteAsync("SET STATISTICS IO OFF;");
        Assert.NotEmpty(lecturas);Assert.True(lecturas.Sum()<5000,$"Lecturas de movimientos: {lecturas.Sum()}. La consulta debe buscar por la identidad contable, no recorrer cien mil filas por cada vínculo.");
        Assert.Equal(2,grupos[1].Movimientos);Assert.Equal(1,grupos[1].Autorizados);
        Assert.Equal(1,grupos[2].Autorizados); // clave antigua con huella exacta
        Assert.Equal(0,grupos[3].Autorizados); // movimiento desaparecido
        Assert.Equal(0,grupos[4].Autorizados); // clave ambigua entre unidades
        Assert.Equal(1,grupos[5].Autorizados); // CUENTA legacy con blancos iniciales
        await cn.ExecuteAsync("UPDATE #Usuarios SET AdminCentral=1 WHERE NOMBRE='ENSAYO'");
        grupos=await Consultar();Assert.Equal(2,grupos[1].Autorizados);Assert.Equal(0,grupos[4].Autorizados);
        await cn.ExecuteAsync("UPDATE #Vinculos SET Huella='OBSOLETA' WHERE VinculoId=2");
        Assert.Equal(0,(await Consultar())[2].Autorizados);
    }
}
