using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesGrabacionTests(ITestOutputHelper output)
{
    [CargaComprobantesSqlFact]
    public async Task FacturaCompletaBalanceaAplicaStockYRollbackRestauraOrigen()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var repo = new CargaComprobantesRepository();
        var ctx = await repo.GetContextoAsync(cn, "Administrador", default);
        var origen = await repo.GetOrigenAsync(cn, ctx, 432549, default);
        var cantidades = origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.Pendiente }).ToList();
        var planner = new CargaComprobantesPlanificador(repo);
        var plan = await planner.CrearAsync(cn, ctx, origen.Id, 10, cantidades, DateTime.Today, default);
        var writer = new CargaComprobantesGrabador(planner, new());
        var previo = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE");
        using (var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                var result = await writer.GrabarAsync(cn, tx, ctx, plan, Guid.NewGuid(), 99999998, "00000000000000", DateTime.Today, default);
                Assert.Equal(3, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE SUCURSAL='1007' AND NUMERO='99999998' AND LETRA=@Letra", new { plan.Letra }, tx));
                Assert.Equal(0, await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE SUCURSAL='1007' AND NUMERO='99999998' AND LETRA=@Letra", new { plan.Letra }, tx));
                Assert.Equal(0, await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(SUM(SALDO),0) FROM VT_V_MV_INSUMOS_SALDOSRM WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante", new { origen.Tc, origen.Comprobante }, tx));
                Assert.Equal(plan.Liquidacion.Lineas.Count, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante", new { result.Comprobante }, tx));
                Assert.True(await cn.ExecuteScalarAsync<bool>("SELECT FINALIZADA FROM V_MV_CPTE WHERE ID=@Id", new { origen.Id }, tx));
                output.WriteLine($"Cabecera, {plan.Liquidacion.Lineas.Count} detalles/stock, asiento balanceado, aplicación y finalización comprobados; rollback obligatorio.");
            }
            finally { if (tx.Connection is not null) tx.Rollback(); }
        }
        Assert.Equal(previo, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE"));
        var restaurado = await repo.GetOrigenAsync(cn, ctx, origen.Id, default);
        Assert.Equal(origen.Lineas.Sum(x => x.Pendiente), restaurado.Lineas.Sum(x => x.Pendiente));
    }
}
