using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ActualizacionOrigenLegacyTests
{
    [OrigenLegacySqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigracionConservaResultadosYPoliticaPrRmDeCadaVariante(bool excluirPrRm)
    {
        var connection=Environment.GetEnvironmentVariable("ORIGEN_LEGACY_TEST_SQL")!;
        Assert.Equal("ELBOLSON2011_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        using var tx=cn.BeginTransaction();
        try
        {
            var definicion=excluirPrRm?"""
                ALTER VIEW [dbo].[VT_V_MV_INSUMOS_APLICACION] AS
                SELECT A.TCO_ORIGEN,A.IDComprobante_Origen AS IdComprobanteOrigen,I.IDARTICULO,SUM(I.CANTIDAD) AS CantidadAplicada
                FROM dbo.V_MV_CpteInsumos AS I INNER JOIN dbo.MV_APLICACION AS A ON I.TC=A.TC AND I.IDCOMPROBANTE=A.IDComprobante
                WHERE (NOT (I.TC='PR')) OR (NOT (A.TCO_ORIGEN='RM'))
                GROUP BY I.IDARTICULO,A.TCO_ORIGEN,A.IDComprobante_Origen
                """:"""
                ALTER VIEW dbo.VT_V_MV_INSUMOS_APLICACION AS
                SELECT dbo.MV_APLICACION.TCO_ORIGEN,dbo.MV_APLICACION.IDComprobante_Origen AS IdComprobanteOrigen,
                dbo.V_MV_CpteInsumos.IDARTICULO,SUM(dbo.V_MV_CpteInsumos.CANTIDAD) AS CantidadAplicada
                FROM dbo.V_MV_CpteInsumos INNER JOIN dbo.MV_APLICACION ON dbo.V_MV_CpteInsumos.TC=dbo.MV_APLICACION.TC
                AND dbo.V_MV_CpteInsumos.IDCOMPROBANTE=dbo.MV_APLICACION.IDComprobante
                GROUP BY dbo.V_MV_CpteInsumos.IDARTICULO,dbo.MV_APLICACION.TCO_ORIGEN,dbo.MV_APLICACION.IDComprobante_Origen
                """;
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("IF OBJECT_ID('dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA','U') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA",transaction:tx));
            await cn.ExecuteAsync(definicion,transaction:tx);
            var antes=await cn.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.VT_V_MV_INSUMOS_APLICACION",transaction:tx,commandTimeout:180);
            await cn.ExecuteAsync("SELECT TOP(2000) * INTO #Antes FROM dbo.VT_V_MV_INSUMOS_APLICACION",transaction:tx,commandTimeout:180);
            var script=await File.ReadAllTextAsync(Script());
            await cn.ExecuteAsync(script,transaction:tx,commandTimeout:180);
            await cn.ExecuteAsync("SELECT * INTO #Despues FROM dbo.VT_V_MV_INSUMOS_APLICACION; CREATE INDEX IX_Despues ON #Despues(TCO_ORIGEN,IdComprobanteOrigen,IDARTICULO);",transaction:tx,commandTimeout:180);
            Assert.Equal(antes,await cn.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM #Despues",transaction:tx,commandTimeout:180));
            Assert.Equal(0,await cn.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM #Antes b WHERE NOT EXISTS(
                    SELECT 1 FROM #Despues v
                    WHERE (v.TCO_ORIGEN=b.TCO_ORIGEN OR (v.TCO_ORIGEN IS NULL AND b.TCO_ORIGEN IS NULL))
                    AND (v.IdComprobanteOrigen=b.IdComprobanteOrigen OR (v.IdComprobanteOrigen IS NULL AND b.IdComprobanteOrigen IS NULL))
                    AND (v.IDARTICULO=b.IDARTICULO OR (v.IDARTICULO IS NULL AND b.IDARTICULO IS NULL))
                    AND (ABS(v.CantidadAplicada-b.CantidadAplicada)<=0.0000001 OR (v.CantidadAplicada IS NULL AND b.CantidadAplicada IS NULL)))
                """,transaction:tx,commandTimeout:180));
            var primera=await cn.ExecuteScalarAsync<string>("SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.VT_V_MV_INSUMOS_APLICACION'))",transaction:tx);
            Assert.NotNull(primera);
            await cn.ExecuteAsync(script,transaction:tx,commandTimeout:180);
            Assert.Equal(primera,await cn.ExecuteScalarAsync<string>("SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.VT_V_MV_INSUMOS_APLICACION'))",transaction:tx));
            Assert.Equal(excluirPrRm,primera.Contains("I.TC<>'PR'",StringComparison.Ordinal));
        }
        finally{if(tx.Connection is not null)tx.Rollback();}
    }
    private static string Script()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"AlfaCore.sln")))dir=dir.Parent;
        return Path.Combine(dir!.FullName,"src/AlfaCore/App_Data/updates/2026-10-17-001__ventas_carga_comprobantes_trazabilidad_origen.sql");
    }
}
public sealed class OrigenLegacySqlTheoryAttribute:TheoryAttribute
{
    public OrigenLegacySqlTheoryAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ORIGEN_LEGACY_TEST_SQL")))Skip="Requiere base de pruebas autorizada en ORIGEN_LEGACY_TEST_SQL; todos los cambios se revierten.";}
}
