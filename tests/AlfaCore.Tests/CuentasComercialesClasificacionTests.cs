using System.Data;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CuentasComercialesClasificacionTests
{
    private const BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
    private static void Parametros(SqlCommand cmd,string clasificacion,CuentaComercialTipo tipo)
    {
        var request=new CuentaComercialSaveRequest{Codigo="ENSAYO",ClasificacionCodigo=clasificacion.Trim()};
        typeof(CuentasComercialesService).GetMethod("FillCuentaAdicParameters",Flags)!.Invoke(null,
            [cmd,request,"1","1","1","1","1",(short)1,"1",tipo,clasificacion]);
    }

    [Theory]
    [InlineData(CuentaComercialTipo.Cliente,"   1")]
    [InlineData(CuentaComercialTipo.Proveedor,"   1")]
    [InlineData(CuentaComercialTipo.Cliente,"A   ")]
    [InlineData(CuentaComercialTipo.Cliente,"")]
    public void ParametroConservaCodigoLegacyYPermiteQuitarClasificacion(CuentaComercialTipo tipo,string codigo)
    {
        using var cmd=new SqlCommand();Parametros(cmd,codigo,tipo);
        var p=cmd.Parameters["@Clasificacion"];
        Assert.Equal(SqlDbType.NVarChar,p.SqlDbType);Assert.Equal(4,p.Size);
        Assert.Equal(codigo.Length==0?(object)DBNull.Value:codigo,p.Value);
    }

    [TarjetasVariasSqlFact]
    public async Task GuardarYReabrirCoincidenConComboAunqueElCatalogoTengaRelleno()
    {
        var connection=Environment.GetEnvironmentVariable("TARJETAS_VARIAS_TEST_SQL")!;
        Assert.Contains(new SqlConnectionStringBuilder(connection).InitialCatalog,new[]{"ELBOLSON2011_PRUEBAS","PRUEBAS02122025"});
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        // Solo temporales: no se altera ningún cliente, proveedor ni catálogo de la base.
        await cn.ExecuteAsync("""
            CREATE TABLE #Clasificaciones(Codigo nvarchar(4),Descripcion nvarchar(50));
            INSERT #Clasificaciones VALUES(N'   1',N'Abono'),(N'A   ',N'Especial');
            CREATE TABLE #Ficha(Clasificacion nvarchar(4));
            INSERT #Ficha VALUES(NULL);
            """);
        var lookupSql=CuentasComercialesService.ClasificacionesLookupSql.Replace("dbo.TA_CLASIFICACIONES","dbo.#Clasificaciones");
        var opciones=(await cn.QueryAsync<CuentaComercialLookupOptionDto>(lookupSql)).ToList();
        Assert.All(opciones,o=>Assert.Equal(o.Codigo.Trim(),o.Codigo));
        foreach(var codigo in new[]{"1","A",""})
        {
            var almacenado=codigo.Length==0?string.Empty:await (Task<string>)typeof(CuentasComercialesService)
                .GetMethod("ResolverCodigoTablaAsync",Flags)!.Invoke(null,[cn,null,"#Clasificaciones","Codigo",codigo,"",CancellationToken.None])!;
            await using var cmd=new SqlCommand("UPDATE #Ficha SET Clasificacion=@Clasificacion;",cn);
            Parametros(cmd,almacenado,CuentaComercialTipo.Cliente);await cmd.ExecuteNonQueryAsync();
            var leido=await cn.ExecuteScalarAsync<string>($"SELECT {CuentasComercialesService.ClasificacionDetalleSql} FROM #Ficha adic;");
            Assert.Equal(codigo,leido);
            if(codigo.Length>0)
            {
                Assert.Contains(opciones,o=>o.Codigo==leido);
                Assert.Equal(8,await cn.ExecuteScalarAsync<int>("SELECT DATALENGTH(Clasificacion) FROM #Ficha;"));
            }
            else Assert.Null(await cn.ExecuteScalarAsync<string?>("SELECT Clasificacion FROM #Ficha;"));
        }
    }
}
