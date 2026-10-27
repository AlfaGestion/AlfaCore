using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesDirectaTests
{
    private static CargaComprobantesDirecta Draft() => new()
    {
        Cliente = "112011541", UnidadNegocio = "4", Deposito = "1", Observaciones = "Ensayo de carga directa con rollback",
        Lineas = [new() { Articulo = "403", Cantidad = 2 }]
    };

    [CargaComprobantesSqlFact]
    public async Task DirectaGrabaTodosLosEfectosSinCrearNiConsumirPedidoYRollbackRestaura()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default, incluirCatalogos: false);
        var planner = new CargaComprobantesPlanificador(repo); var draft = Draft();
        var plan = await planner.CrearAsync(cn, ctx, 0, 10, [], DateTime.Today, default, directa: draft);
        Assert.Equal(0, plan.OrigenId); Assert.Equal("DIRECTA", plan.Origen.Tc); Assert.Equal(draft.Borrador, plan.Directa!.Borrador);
        Assert.Equal(2m, Assert.Single(plan.Liquidacion.Lineas).Cantidad);
        var previo = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE");
        using (var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                var result = await new CargaComprobantesGrabador(planner, new()).GrabarAsync(cn, tx, ctx, plan, Guid.NewGuid(),
                    99999998, "00000000000000", DateTime.Today, default);
                var p = new { result.Comprobante, Id = result.FacturaId };
                Assert.Equal(previo + 1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE", transaction: tx));
                Assert.Equal(2m, await cn.ExecuteScalarAsync<decimal>("SELECT CANTIDAD FROM V_MV_CPTEINSUMOS WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante", p, tx));
                Assert.Equal(-2m, await cn.ExecuteScalarAsync<decimal>("SELECT CANTIDAD FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante", p, tx));
                Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_APLICACION WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante", p, tx));
                Assert.Equal(3, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE TC='FC' AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA=RIGHT(@Comprobante,1)", p, tx));
                Assert.Equal(0m, await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE TC='FC' AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA=RIGHT(@Comprobante,1)", p, tx));
                Assert.Equal(plan.Liquidacion.Total, await cn.ExecuteScalarAsync<decimal>("SELECT IMPORTE FROM V_MV_CPTE WHERE ID=@Id", p, tx));
                Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE WHERE ID=@Id AND (NULLIF(LTRIM(RTRIM(TCORIGEN)),'') IS NOT NULL OR NULLIF(LTRIM(RTRIM(COMPROBANTEORIGEN)),'') IS NOT NULL)", p, tx));
            }
            finally { if (tx.Connection is not null) tx.Rollback(); }
        }
        Assert.Equal(previo, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE"));
    }

    [CargaComprobantesSqlFact]
    public async Task CambiosDeArticuloOUnidadInvalidanLaCargaAntesDeGrabar()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default, incluirCatalogos: false);
        var planner = new CargaComprobantesPlanificador(repo); var draft = Draft();
        var plan = await planner.CrearAsync(cn, ctx, 0, 10, [], DateTime.Today, default, directa: draft);
        using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync("UPDATE V_MA_ARTICULOS SET COSTO=ISNULL(COSTO,0)+1 WHERE LTRIM(RTRIM(IDARTICULO))='403'", transaction: tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => new CargaComprobantesGrabador(planner, new())
                .GrabarAsync(cn, tx, ctx, plan, Guid.NewGuid(), 99999998, "00000000000000", DateTime.Today, default));
            var restringido = new CargaComprobantesContexto { Usuario = "Administrador", UnidadNegocio = "3", AdminCentral = false };
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => new CargaComprobantesDirectaPreparador()
                .PrepararAsync(cn, restringido, draft, default, tx));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    [CargaComprobantesSqlFact]
    public async Task BorradorImpideDobleEmisionYConservaPendienteAnteCambioDeSesion()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default, incluirCatalogos: false);
        var planner = new CargaComprobantesPlanificador(repo); var draft = Draft();
        var wsaa = DispatchProxy.Create<IWsaaClient, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)wsaa).Handler = (_, _) => Task.FromResult(new WsaaTicket("test", "test", DateTime.UtcNow.AddMinutes(10)));
        var wsfe = DispatchProxy.Create<IWsfev1Client, CargaComprobantesUiTests.EventOnlyProxy>(); var envios = 0;
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)wsfe).Handler = (m, args) =>
        {
            if (m.Name == "ObtenerUltimoAutorizadoAsync") return Task.FromResult(99999996L);
            Assert.Equal("SolicitarCaeAsync", m.Name); envios++; var s = (ArcaCaeSolicitudDto)args![1]!;
            return Task.FromResult(new ArcaCaeResultadoDto(true, "A", s.CbteDesde, s.CbteHasta, "00000000000000", DateTime.Today, [], null));
        };
        var service = new CargaComprobantesEmisionService(planner, new(planner, new()), new(runtime.Config, wsaa, wsfe, runtime.Events), runtime.Config, runtime.Events);
        var plan = await service.ValidarAsync(cn, ctx, 0, 10, [], default, draft);
        var op = Guid.NewGuid(); var intentos = 0; var previo = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE");
        try
        {
            Assert.Null(await service.ConsultarDirectaAsync(cn, ctx, draft.Borrador, op, default));
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.EmitirAsync(cn, ctx, 0, 10, [], Guid.NewGuid(), "DESACTUALIZADA", () => { }, default, draft));
            Assert.Equal(0, envios);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.EmitirAsync(cn, ctx, 0, 10, [], op, plan.Huella,
                () => { if (++intentos > 1) throw new CargaComprobantesReglaException("Cambio de sesión de prueba"); }, default, draft));
            var pendiente = await service.GetAsync(cn, ctx, op, default);
            Assert.Equal("PENDIENTE_GRABACION", pendiente!.Estado); Assert.Equal(draft.Borrador, pendiente.Borrador);
            Assert.Equal(op, (await service.ConsultarDirectaAsync(cn, ctx, draft.Borrador, op, default))!.Operacion);
            var repetida = await service.EmitirAsync(cn, ctx, 0, 10, [], Guid.NewGuid(), plan.Huella, () => { }, default, draft);
            Assert.Equal(op, repetida.Operacion); Assert.Equal(1, envios);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.EmitirAsync(cn, ctx, 0, 10, [], op,
                plan.Huella, () => { }, default, Draft()));
            Assert.Equal(previo, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE"));
        }
        finally
        {
            await cn.ExecuteAsync("DELETE FROM ALFACORE_VENTAS_CARGA_OPERACION WHERE Operacion=@Op; DELETE FROM ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Op", new { Op = op });
        }
    }
}
