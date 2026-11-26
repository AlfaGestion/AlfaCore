using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using static AlfaCore.Tests.PlanCuentasAbmSqlTests;

namespace AlfaCore.Tests;

public class CobranzaSaldosFacturasSqlTests
{
    /// <summary>
    /// Contra una base real (COMPROBANTES_TEST_SQL): la consulta optimizada de saldos por factura da exactamente lo mismo
    /// que agrupar la vista del legacy dbo.VE_CPTES_SALDOS_VENTAS quedándose con FC/FP/ND. Solo lectura.
    /// </summary>
    [ComprobantesSqlFact]
    public async Task DaLoMismoQueLaVistaDelLegacy()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")!);
        var sql = $"""
            SET NOCOUNT ON;
            SELECT CUENTA,TC,SUCURSAL,NUMERO,LETRA,SUM(SALDO) SALDO,MIN(ISNULL(VENCIMIENTO,FECHA)) VENCIMIENTO INTO #viejo
            FROM dbo.VE_CPTES_SALDOS_VENTAS GROUP BY CUENTA,TC,SUCURSAL,NUMERO,LETRA HAVING CONVERT(decimal(18,2),SUM(SALDO))>0;
            DELETE FROM #viejo WHERE TC NOT IN ('FC','FP','ND');
            SELECT * INTO #nuevo FROM ({CobranzasService.SaldosFacturasSql}) n;
            SELECT (SELECT COUNT(*) FROM #viejo),
                   (SELECT COUNT(*) FROM (SELECT * FROM #viejo EXCEPT SELECT * FROM #nuevo) x),
                   (SELECT COUNT(*) FROM (SELECT * FROM #nuevo EXCEPT SELECT * FROM #viejo) x);
            """;
        var (total, soloViejo, soloNuevo) = await cn.QuerySingleAsync<(int, int, int)>(
            new CommandDefinition(sql, new { cliente = "" }, commandTimeout: 600));
        Assert.True(total > 0, "La base de prueba no tiene facturas pendientes: el test no compara nada.");
        Assert.Equal((0, 0), (soloViejo, soloNuevo));
    }
}
