using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Carga directa de NCFP, presupuestos, notas de pedido y remitos (no electrónicos).</summary>
public sealed class CargaComprobantesTiposNoElectronicosTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("FP", "X", false, "X")]
    [InlineData("RM", "XR", false, "X")]
    [InlineData("RM", "R", true, "R")]
    [InlineData("NP", "", true, "X")]
    [InlineData("NCFP", "ABM", true, "A")]
    [InlineData("NCFP", "ABM", false, "B")]
    [InlineData("NCFP", "BM", true, "M")]
    [InlineData("NCFP", "X", true, "X")]
    [InlineData("PR", "X", false, "X")]
    [InlineData("PR", "AB", false, "B")]
    public void LetraSegunLasLetrasHabilitadas(string tc, string letras, bool requiereA, string esperada)
        => Assert.Equal(esperada, CargaComprobantesTipos.LetraNoFiscal(tc, letras, requiereA));

    [Fact]
    public void EfectosPorTipo()
    {
        Assert.True(CargaComprobantesTipos.GeneraAsiento("NCFP", false));
        Assert.False(CargaComprobantesTipos.GeneraAsiento("NCFP", true));
        foreach (var tc in new[] { "PR", "NP", "RM" })
        {
            Assert.False(CargaComprobantesTipos.GeneraAsiento(tc, false));
            Assert.False(CargaComprobantesTipos.EsElectronico(tc));
            Assert.True(CargaComprobantesTipos.EsEditable(tc));
        }
        Assert.Equal("FP", CargaComprobantesTipos.TcAsociado("NCFP"));
        Assert.True(CargaComprobantesTipos.RequiereAsociado("NCFP"));
        Assert.True(CargaComprobantesTipos.EsCredito("NCFP"));
    }

    /// <summary>
    /// Valida (ensayo con rollback, sin grabar) PR/NP/RM/FP y una NCFP sobre una FP existente.
    /// Requiere ALFACORE_TEST_TIPOS=1 y ALFACORE_TEST_SQL hacia una base de prueba/demo.
    /// </summary>
    [Fact]
    public async Task ValidaTiposNoElectronicosContraLaBase()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL");
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_TIPOS") != "1" || string.IsNullOrWhiteSpace(cs)) return;
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        var repo = new CargaComprobantesRepository();
        var planner = new CargaComprobantesPlanificador(repo);
        var sender = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var emision = new CargaComprobantesEmisionService(planner, new(planner, new()), sender, runtime.Config, runtime.Events);
        var usuario = Environment.GetEnvironmentVariable("ALFACORE_TEST_USUARIO") ?? "Administrador";
        var ctx = await repo.GetContextoAsync(cn, usuario, CancellationToken.None);
        var unidad = ctx.UnidadNegocio.Length > 0 ? ctx.UnidadNegocio : ctx.UnidadesNegocio[0].Codigo;
        var deposito = ctx.Depositos.OrderBy(x => x.Grupo == unidad ? 0 : 1).First().Codigo;

        // Cliente de cuenta corriente simple y artículo simple con precio.
        var fp = await cn.QueryFirstOrDefaultAsync<(string Cuenta, string Articulo)>("""
            SELECT TOP (1) LTRIM(RTRIM(cl.CODIGO)), (SELECT TOP (1) LTRIM(RTRIM(a.IDARTICULO)) FROM dbo.V_MA_ARTICULOS a
                WHERE ISNULL(a.PRECIO1,0)>0 AND ISNULL(a.Suspendido,0)=0 AND ISNULL(a.SuspendidoV,0)=0
                AND ISNULL(a.USASERIE,0)=0 AND ISNULL(a.USALOTE,0)=0 AND ISNULL(a.UsaTalle,0)=0 AND ISNULL(a.PideEquivalencia,0)=0
                AND ISNULL(a.PideMedidas,0)=0 AND ISNULL(a.EnComodato,0)=0 AND ISNULL(a.IMPUESTOS,0)=0 AND ISNULL(a.ITC,0)=0 AND ISNULL(a.EnOferta,0)=0
                AND ISNULL(a.EXENTO,0)=0 AND ISNULL(LTRIM(RTRIM(a.CuentaContable)),'')='' AND ISNULL(LTRIM(RTRIM(a.IdPercepcion)),'')=''
                AND ISNULL(LTRIM(RTRIM(a.Moneda)),'1') IN ('','1') AND ISNULL(a.Dto1,0)=0 AND ISNULL(a.Rec1,0)=0 AND ISNULL(LTRIM(RTRIM(a.IDUNIDAD)),'')<>''
                ORDER BY a.IDARTICULO)
            FROM dbo.VT_CLIENTES cl
            JOIN dbo.V_TA_Cpra_Vta cond ON LTRIM(RTRIM(cond.IDCond_Cpra_Vta))=LTRIM(RTRIM(cl.idCond_Cpra_Vta))
                AND cond.CantidadDias>0 AND ISNULL(cond.PorcRecargo,0)=0
                AND NOT EXISTS (SELECT 1 FROM dbo.V_TA_Cpra_VtaDias d WHERE LTRIM(RTRIM(d.IDCOND_CPRA_VTA))=LTRIM(RTRIM(cond.IDCond_Cpra_Vta)))
            WHERE ISNULL(cl.BLOQUEO,0)=0 AND ISNULL(cl.Dada_De_Baja,0)=0 AND ISNULL(cl.Limite_Credito,0)=0
                AND ISNULL(cl.ExentoIvaArticulos,0)=0 AND ISNULL(cl.Descuento,0)=0
            ORDER BY cl.CODIGO;
            """);
        output.WriteLine($"Cliente {fp.Cuenta} · artículo {fp.Articulo} · unidad {unidad} · depósito {deposito}");
        Assert.False(string.IsNullOrEmpty(fp.Articulo));

        var errores = new List<string>();
        CargaComprobantesPlanGrabacion? planFp = null;
        foreach (var tc in new[] { "PR", "NP", "RM", "FP" })
        {
            var talonario = ctx.TalonariosDirecta.FirstOrDefault(x => x.Tc == tc && (x.UnidadNegocio.Length == 0 || x.UnidadNegocio == unidad));
            if (talonario is null) { output.WriteLine($"{tc}: sin talonario"); continue; }
            var draft = new CargaComprobantesDirecta
            {
                Borrador = Guid.NewGuid(), Tc = tc, Cliente = fp.Cuenta, UnidadNegocio = unidad, Deposito = deposito,
                Observaciones = "Prueba AlfaCore " + tc,
                Lineas = [new() { Articulo = fp.Articulo, Cantidad = 1 }]
            };
            try
            {
                var plan = await emision.ValidarAsync(cn, ctx, 0, talonario.Talonario, [], CancellationToken.None, draft);
                output.WriteLine($"{tc}: OK {plan.Letra} {plan.PuntoVenta:0000} · total {plan.Liquidacion.Total:N2} · IVA {plan.Liquidacion.Iva:N2} · " +
                    $"asiento {(plan.GeneraAsiento ? plan.TcContable : "no")} · stock {(plan.DescargaStock ? plan.SignoStock.ToString() : "no")} · asociado {plan.AsociadoComprobante}");
                Assert.False(plan.EsElectronico);
                Assert.Equal(tc is "FP" or "NCFP", plan.GeneraAsiento);
                if (tc is "PR" or "NP") Assert.False(plan.DescargaStock);
                if (tc == "FP") planFp = plan;
            }
            catch (CargaComprobantesReglaException ex) { errores.Add($"{tc}: {ex.Message}"); output.WriteLine($"{tc}: {ex.Message}"); }
        }
        Assert.Empty(errores);
        if (planFp is null) return;

        // NCFP sobre una FP grabada en la misma transacción; todo se revierte.
        var writer = new CargaComprobantesGrabador(planner, new());
        var talNcfp = ctx.TalonariosDirecta.First(x => x.Tc == "NCFP" && (x.UnidadNegocio.Length == 0 || x.UnidadNegocio == unidad));
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var grabadaFp = await writer.GrabarAsync(cn, tx, ctx, planFp, Guid.NewGuid(), 99999990, "", planFp.Fecha, CancellationToken.None);
            var nota = new CargaComprobantesDirecta
            {
                Borrador = Guid.NewGuid(), Tc = "NCFP", Cliente = fp.Cuenta, UnidadNegocio = unidad, Deposito = deposito,
                FacturaAsociadaId = grabadaFp.FacturaId, Observaciones = "Prueba AlfaCore NCFP", Lineas = [new() { Articulo = fp.Articulo, Cantidad = 1 }]
            };
            var planNc = await planner.CrearAsync(cn, ctx, 0, talNcfp.Talonario, [], planFp.Fecha, CancellationToken.None, tx, nota);
            output.WriteLine($"NCFP: {planNc.Letra} {planNc.PuntoVenta:0000} · total {planNc.Liquidacion.Total:N2} · asiento {planNc.TcContable} · stock {planNc.SignoStock} · asociado {planNc.AsociadoComprobante}");
            Assert.Equal(planFp.Liquidacion.Total, planNc.Liquidacion.Total);
            Assert.Equal(1, planNc.SignoStock);
            var grabadaNc = await writer.GrabarAsync(cn, tx, ctx, planNc, Guid.NewGuid(), 99999990, "", planNc.Fecha, CancellationToken.None);
            var efectos = await cn.QuerySingleAsync<(string TcOrigen, int Aplicaciones, int Asientos, int ConLibro, decimal Stock)>("""
                SELECT LTRIM(RTRIM(c.TCORIGEN)),
                    (SELECT COUNT(*) FROM dbo.MV_APLICACION a WHERE a.TC=c.TC AND a.IDCOMPROBANTE=c.IDCOMPROBANTE AND LTRIM(RTRIM(a.TCO_ORIGEN))='FP'),
                    (SELECT COUNT(*) FROM dbo.MV_ASIENTOS m WHERE LTRIM(RTRIM(m.TC))=@TcCont AND m.SUCURSAL=c.SUCURSAL AND m.NUMERO=c.NUMERO AND m.LETRA=c.LETRA),
                    (SELECT COUNT(*) FROM dbo.MV_ASIENTOS m WHERE LTRIM(RTRIM(m.TC))=@TcCont AND m.SUCURSAL=c.SUCURSAL AND m.NUMERO=c.NUMERO AND m.LETRA=c.LETRA AND m.LIVA_TIPO IS NOT NULL),
                    ISNULL((SELECT SUM(s.Cantidad) FROM dbo.V_MV_STOCK s WHERE s.TC=c.TC AND s.IDCOMPROBANTE=c.IDCOMPROBANTE),0)
                FROM dbo.V_MV_CPTE c WHERE c.ID=@Id;
                """, new { Id = grabadaNc.FacturaId, TcCont = planNc.TcContable }, tx);
            output.WriteLine($"NCFP grabada {grabadaNc.Comprobante} · origen {efectos.TcOrigen} · aplicaciones {efectos.Aplicaciones} · renglones asiento {efectos.Asientos} · libro IVA {efectos.ConLibro} · stock {efectos.Stock}");
            Assert.Equal("FP", efectos.TcOrigen);
            Assert.Equal(1, efectos.Aplicaciones);
            Assert.True(efectos.Asientos >= 2);
            Assert.Equal(0, efectos.ConLibro);
            Assert.Equal(1, efectos.Stock);
            Assert.NotNull(grabadaNc.NumeroAsiento);
        }
        finally { await tx.RollbackAsync(); }
    }
}
