using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesTiposDirectaTests
{
    private static CargaComprobantesDirecta Draft(string tc, int? asociado = null) => new()
    {
        Tc = tc, FacturaAsociadaId = asociado, Cliente = "112011541", UnidadNegocio = "4", Deposito = "1",
        Lineas = [new() { Articulo = "403", Cantidad = 1 }]
    };

    [CargaComprobantesSqlFact]
    public async Task ProformaConYSinIvaSiempreGeneraAsientoSinDatosDelLibroNiCae()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default, incluirCatalogos: false);
        var planner = new CargaComprobantesPlanificador(repo); var writer = new CargaComprobantesGrabador(planner, new());
        var previo = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE");
        foreach (var sumar in new[] { "SI", "NO" })
        {
            using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                // Solo cambia las dos claves precisas dentro de un ensayo revertido.
                Assert.Equal(1, await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR=@Valor WHERE CLAVE='ROBAIVAPROFORMA'", new { Valor = sumar }, tx));
                await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='SI' WHERE CLAVE='USAFACTURAPROFORMASINREGISTRACION'", transaction: tx);
                var plan = await planner.CrearAsync(cn, ctx, 0, 0, [], DateTime.Today, default, tx, Draft("FP"));
                Assert.False(plan.EsElectronico); Assert.Equal("X", plan.Letra); Assert.Equal(4, plan.PuntoVenta);
                Assert.Equal(sumar == "SI", plan.Liquidacion.Iva > 0);
                var result = await writer.GrabarAsync(cn, tx, ctx, plan, Guid.NewGuid(), 99999998, "", DateTime.Today, default);
                var p = new { result.Comprobante };
                Assert.Null(result.Cae); Assert.NotNull(result.NumeroAsiento);
                Assert.Equal(sumar == "SI" ? 3 : 2, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE TC='FP' AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='X'", p, tx));
                Assert.Equal(0, await cn.ExecuteScalarAsync<int>("""
                    SELECT COUNT(*) FROM MV_ASIENTOS WHERE TC='FP' AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='X'
                        AND (LIVA_TIPO IS NOT NULL OR CABCUENTA IS NOT NULL OR CABNOMBRE IS NOT NULL OR CABCUIT IS NOT NULL
                            OR LIVA_ImpNetoGrav IS NOT NULL OR LIVA_ImpIVA IS NOT NULL OR LIVA_TOTAL IS NOT NULL OR NroCAI IS NOT NULL);
                    """, p, tx));
                Assert.Equal(0m, await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE TC='FP' AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='X'", p, tx));
                Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE_ELECTRONICOS WHERE TC='FP' AND IDCOMPROBANTE=@Comprobante", p, tx));
            }
            finally { if (tx.Connection is not null) tx.Rollback(); }
        }
        Assert.Equal(previo, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE"));
        Assert.Equal("SI", await cn.ExecuteScalarAsync<string>("SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE='ROBAIVAPROFORMA'"));
        Assert.Equal("NO", await cn.ExecuteScalarAsync<string>("SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE='USAFACTURAPROFORMASINREGISTRACION'"));
    }

    [CargaComprobantesSqlFact]
    public async Task NotasInviertenAsientoYStockSegunTalonarioYConservanElAsociado()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default, incluirCatalogos: false);
        var planner = new CargaComprobantesPlanificador(repo); var writer = new CargaComprobantesGrabador(planner, new());
        var factura = await cn.ExecuteScalarAsync<int>("SELECT ID FROM V_MV_CPTE WHERE TC='FC' AND IDCOMPROBANTE='100700000186A'");
        foreach (var tipo in new[] { "NC", "ND" })
        {
            var plan = await planner.CrearAsync(cn, ctx, 0, tipo == "NC" ? 7 : 6, [], DateTime.Today, default, directa: Draft(tipo, factura));
            Assert.NotNull(plan.Asociado); Assert.Equal(186, plan.Asociado!.Numero); Assert.Equal(tipo == "NC" ? 3 : 2, plan.TipoArca);
            if (tipo == "NC") Assert.Equal(122.45m, plan.Liquidacion.Total); // Precio histórico de la factura, no lista actual.
            using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                var result = await writer.GrabarAsync(cn, tx, ctx, plan, Guid.NewGuid(), 99999998, "00000000000000", DateTime.Today, default);
                var p = new { Tc = tipo, result.Comprobante };
                Assert.Equal(tipo == "NC" ? 1m : -1m, await cn.ExecuteScalarAsync<decimal>("SELECT CANTIDAD FROM V_MV_STOCK WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante", p, tx));
                var libro = await cn.QuerySingleAsync<Libro>("""
                    SELECT [DEBE-HABER] Dh,LIVA_TOTAL Total,LIVA_ImpIVA Iva FROM MV_ASIENTOS
                    WHERE TC=@Tc AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='A' AND SECUENCIA=1;
                    """, p, tx);
                Assert.Equal(tipo == "NC" ? "H" : "D", libro.Dh);
                Assert.Equal((tipo == "NC" ? -1 : 1) * plan.Liquidacion.Total, libro.Total);
                Assert.Equal(plan.Liquidacion.Total, await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE FROM MV_APLICACION WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante", p, tx));
                Assert.Equal(0m, await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE TC=@Tc AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='A'", p, tx));
                if (tipo == "NC")
                {
                    Assert.Equal(plan.Origen.Lineas[0].ReferenciaOrigenId, await cn.ExecuteScalarAsync<int>(
                        "SELECT Id_Origen FROM V_MV_STOCK WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante", p, tx));
                    Assert.Equal(plan.Origen.Lineas[0].CostoReferencia, await cn.ExecuteScalarAsync<decimal>(
                        "SELECT COSTO FROM V_MV_CPTEINSUMOS WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante", p, tx));
                    Assert.Equal(plan.Origen.Lineas[0].EquivalenciaReferencia, await cn.ExecuteScalarAsync<decimal>(
                        "SELECT EQUIV_UDBASE FROM V_MV_CPTEINSUMOS WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante", p, tx));
                    await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => planner.CrearAsync(cn, ctx, 0, 7, [], DateTime.Today, default, tx, Draft("NC", factura)));
                }
            }
            finally { if (tx.Connection is not null) tx.Rollback(); }
        }
        var incorrecta = Draft("NC", factura); incorrecta.Lineas[0].Cantidad = 2;
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => planner.CrearAsync(cn, ctx, 0, 7, [], DateTime.Today, default, directa: incorrecta));
        incorrecta = Draft("ND", factura); incorrecta.UnidadNegocio = "3";
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => planner.CrearAsync(cn, ctx, 0, 6, [], DateTime.Today, default, directa: incorrecta));
    }

    private sealed class Libro
    {
        public string Dh { get; set; } = "";
        public decimal Total { get; set; }
        public decimal Iva { get; set; }
    }
}
