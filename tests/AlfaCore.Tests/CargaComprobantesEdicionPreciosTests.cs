using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesEdicionPreciosTests
{
    [Theory]
    [InlineData(20, 10, 5, 5)]
    [InlineData(5, 10, 8, 5)]
    [InlineData(20, 10, 0, 10)]
    [InlineData(20, 0, 0, 20)]
    [InlineData(20, 0, 5, 0)]
    [InlineData(0, 10, 0, 10)]
    [InlineData(0, 0, 0, 0)]
    public void ResuelveMaximosSinConvertirCeroEnPermisoIlimitado(decimal general, decimal condicion, decimal vendedor, decimal esperado)
        => Assert.Equal(esperado, CargaComprobantesEdicionPrecios.ResolverMaximo(general, condicion, vendedor));

    [Fact]
    public void RebajaYDescuentoSeControlanJuntosYNoSeCompensanPermisos()
    {
        var l = new CargaComprobantesDirectaLinea { Articulo = "PAPEL", Cantidad = 2, PrecioUnitarioNeto = 95, Descuento = 10 };
        var p = new CargaComprobantesPoliticaPrecio(true, true, 10);
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesEdicionPrecios.Aplicar(l, "FC", p, 100, 121, 21, 2));
        l.Descuento = 5;
        var result = CargaComprobantesEdicionPrecios.Aplicar(l, "FC", p, 100, 121, 21, 2);
        Assert.Equal(95m, result.Neto); Assert.Equal(114.95m, result.Final); Assert.Equal(5m, result.Descuento);
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesEdicionPrecios.Aplicar(l, "FC", p with { PuedeEditarPrecio = false }, 100, 121, 21, 2));
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesEdicionPrecios.Aplicar(l, "FC", p with { PuedeEditarDescuento = false }, 100, 121, 21, 2));
        l.PrecioUnitarioNeto = 200; l.Descuento = 15;
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesEdicionPrecios.Aplicar(l, "FC", p, 100, 121, 21, 2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1.00001, 0)]
    [InlineData(100, -1)]
    [InlineData(100, 100)]
    [InlineData(100, .00001)]
    public void DatosFueraDeRangoNoSeRedondeanSilenciosamente(decimal neto, decimal descuento)
        => Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesEdicionPrecios.Aplicar(
            new() { Articulo = "PAPEL", Cantidad = 1, PrecioUnitarioNeto = neto, Descuento = descuento }, "FC", new(true, true, 99), 100, 121, 21, 2));

    [Fact]
    public void ProformaDistingueTopeEfectivoYDeColumnaYNotasNoAceptanOverride()
    {
        var l = new CargaComprobantesDirectaLinea { Articulo = "PAPEL", Cantidad = 1, Descuento = 15 };
        var p = new CargaComprobantesPoliticaPrecio(true, true, 30, 10);
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesEdicionPrecios.Aplicar(l, "FP", p, 100, 121, 21, 2));
        l.Descuento = null; l.PrecioUnitarioNeto = 80;
        Assert.Equal(80m, CargaComprobantesEdicionPrecios.Aplicar(l, "FP", p, 100, 121, 21, 2).Neto);
        foreach (var tc in new[] { "NC", "ND" })
            Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesEdicionPrecios.Aplicar(l, tc, p, 100, 121, 21, 2));
    }

    [Fact]
    public void CamposNoUsadosConservanJsonAnteriorYEdicionQuedaEnSnapshot()
    {
        var l = new CargaComprobantesDirectaLinea { Articulo = "403", Cantidad = 2 };
        Assert.Equal("{\"Articulo\":\"403\",\"Cantidad\":2}", JsonSerializer.Serialize(l));
        l.PrecioUnitarioNeto = 90; l.Descuento = 10;
        var copia = JsonSerializer.Deserialize<CargaComprobantesDirectaLinea>(JsonSerializer.Serialize(l))!;
        Assert.Equal(90m, copia.PrecioUnitarioNeto); Assert.Equal(10m, copia.Descuento);
    }

    private static CargaComprobantesDirecta Draft() => new() { Cliente = "112011541", UnidadNegocio = "4", Deposito = "1",
        Lineas = [new() { Articulo = "403", Cantidad = 2, PrecioUnitarioNeto = 90, Descuento = 10 }] };

    [CargaComprobantesSqlFact]
    public async Task PrecioYDescuentoLleganADetalleStockAsientoYRollback()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", cn.Database);
        var ctx = await new CargaComprobantesRepository().GetContextoAsync(cn, "Administrador", default, incluirCatalogos: false);
        var planner = new CargaComprobantesPlanificador(new()); var draft = Draft();
        var plan = await planner.CrearAsync(cn, ctx, 0, 10, [], DateTime.Today, default, directa: draft);
        Assert.Equal(196.02m, plan.Liquidacion.Total);
        Assert.NotNull(plan.Origen.Lineas[0].PrecioReferenciaNeto);
        using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            var r = await new CargaComprobantesGrabador(planner, new()).GrabarAsync(cn, tx, ctx, plan,
                Guid.NewGuid(), 99999998, "00000000000000", DateTime.Today, default);
            var detalle = await cn.QuerySingleAsync("SELECT IMPORTE_S_IVA Neto,PorcDto Descuento,ImporteDto DtoUnitario,TotalFinal Total FROM V_MV_CPTEINSUMOS WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante", r, tx);
            Assert.Equal(90m, (decimal)detalle.Neto); Assert.Equal(10m, (decimal)detalle.Descuento);
            Assert.Equal(9m, (decimal)detalle.DtoUnitario); Assert.Equal(196.02m, (decimal)detalle.Total);
            Assert.Equal(10m, await cn.ExecuteScalarAsync<decimal>("SELECT DtoItem FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante", r, tx));
            Assert.Equal(0m, await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE TC='FC' AND SUCURSAL='1007' AND NUMERO='99999998' AND LETRA='A'", transaction: tx));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
        Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE WHERE TC='FC' AND IDCOMPROBANTE='100799999998A'"));
    }

    [CargaComprobantesSqlFact]
    public async Task RevocarPermisoInvalidaPlanAntesDeEscribirAunqueContextoSeaAdministrador()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default, incluirCatalogos: false);
        var draft = Draft(); var planner = new CargaComprobantesPlanificador(repo);
        var plan = await planner.CrearAsync(cn, ctx, 0, 10, [], DateTime.Today, default, directa: draft);
        using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET V_ModificaPrecio=0 WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'", transaction: tx);
            ctx.PuedeEditarPrecio = true; ctx.AdminCentral = true;
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => new CargaComprobantesGrabador(planner,new())
                .GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default));
            draft.Lineas[0].PrecioUnitarioNeto = null;
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET V_Descuentos=0 WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'", transaction: tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => new CargaComprobantesDirectaPreparador().PrepararAsync(cn,ctx,draft,default,tx));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    [CargaComprobantesSqlFact]
    public async Task ClienteCondicionActualLimitaEdicionYPrecioVigenteSeRelee()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var ctx = await new CargaComprobantesRepository().GetContextoAsync(cn,"Administrador",default,incluirCatalogos:false);
        using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync("UPDATE v SET DtoMaximo=5 FROM V_TA_Cpra_Vta v JOIN VT_CLIENTES c ON LTRIM(RTRIM(v.IDCOND_CPRA_VTA))=LTRIM(RTRIM(c.IDCOND_CPRA_VTA)) WHERE LTRIM(RTRIM(c.CODIGO))='112011541'",transaction:tx);
            var draft = Draft(); draft.Lineas[0].PrecioUnitarioNeto = null;
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => new CargaComprobantesDirectaPreparador().PrepararAsync(cn,ctx,draft,default,tx));
            var politica = await new CargaComprobantesEdicionPrecios().LeerAsync(cn,"Administrador",draft.Cliente,"FC",default,tx);
            Assert.Equal(5m,politica.MaximoDescuento);
            draft.Lineas[0].Descuento = null; draft.Lineas[0].PrecioUnitarioNeto = 90;
            var planner = new CargaComprobantesPlanificador(new());
            var plan = await planner.CrearAsync(cn,ctx,0,10,[],DateTime.Today,default,tx,draft);
            await cn.ExecuteAsync("UPDATE V_MA_ARTICULOS SET PRECIO1=ISNULL(PRECIO1,0)+1 WHERE LTRIM(RTRIM(IDARTICULO))='403'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => new CargaComprobantesGrabador(planner,new())
                .GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }
}
