using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

internal static class CompraDocumentoSql
{
    // El auxiliar puede faltar antes de aplicar Compras; no se referencia estáticamente en esas bases.
    internal const string PrefijoListado="""
        IF OBJECT_ID('tempdb..#ComprasRegistradas') IS NOT NULL DROP TABLE #ComprasRegistradas;
        CREATE TABLE #ComprasRegistradas(DocumentoId bigint PRIMARY KEY,CompraId int,FechaHora datetime,Usuario nvarchar(100));
        IF OBJECT_ID(N'dbo.ALFACORE_COMPRA_OPERACION',N'U') IS NOT NULL
        EXEC(N'INSERT #ComprasRegistradas SELECT DocumentoId,CompraId,FechaHora,Usuario FROM dbo.ALFACORE_COMPRA_OPERACION WHERE DocumentoId IS NOT NULL;');
        """;
    internal static async Task<int?> CompraIdAsync(SqlConnection cn,long documento,CancellationToken ct)
    {
        if(await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID('dbo.ALFACORE_COMPRA_OPERACION');",cancellationToken:ct))==0)return null;
        return await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("SELECT CompraId FROM dbo.ALFACORE_COMPRA_OPERACION WHERE DocumentoId=@documento;",new{documento},cancellationToken:ct));
    }
}
