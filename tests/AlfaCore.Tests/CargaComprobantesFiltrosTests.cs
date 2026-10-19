using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesFiltrosTests
{
    [CargaComprobantesSqlFact]
    public async Task ListadoYCargaDirectaExcluyenCabecerasAbiertasSinSaldo()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        var repo = new CargaComprobantesRepository();
        var ctx = await repo.GetContextoAsync(cn, Environment.GetEnvironmentVariable("ALFACORE_TEST_USUARIO") ?? "Administrador", default);
        var page = await repo.BuscarAsync(cn, ctx, new() { PageSize = 100 }, default);
        Assert.NotEmpty(page.Items);
        // Verificar la página en una sola lectura independiente de la vista oficial.
        // Repetir la agregación histórica por cada uno de los 100 documentos saturaba
        // las reservas de memoria del servidor de prueba.
        var conSaldo = (await cn.QueryAsync<string>("""
            SELECT DISTINCT LTRIM(RTRIM(IDCOMPROBANTE)) FROM dbo.VT_V_MV_INSUMOS_SALDOSRM
            WHERE LTRIM(RTRIM(TC))='NP' AND TC IN ('NP',' NP','  NP')
                AND IDCOMPROBANTE IN @Comprobantes AND SALDO>0;
            """, new { Comprobantes = page.Items.Select(x => x.Comprobante).ToArray() })).ToHashSet();
        foreach (var doc in page.Items)
        {
            Assert.Equal("NP", doc.Tc);
            Assert.Contains(doc.Comprobante, conSaldo);
        }
        var candidatas = (await cn.QueryAsync<CargaComprobantesOrigen>("""
            SELECT c.ID Id,LTRIM(RTRIM(c.TC)) Tc,LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante
            FROM dbo.V_MV_CPTE c WHERE c.IDCOMPLEMENTO=0 AND c.TC IN ('NP','RM')
                AND c.ANULADA=0 AND c.FINALIZADA=0 AND ISNULL(c.BLOQUEADA,0)=0 AND ISNULL(c.CERRADA,0)=0
                AND ISNULL(c.EnComodato,0)=0 AND (@AdminCentral=1 OR LTRIM(RTRIM(c.UNEGOCIO))=@UnidadNegocio)
                AND LEN(LTRIM(RTRIM(c.IDCOMPROBANTE)))=13
            ORDER BY c.ID;
            """, new { ctx.AdminCentral, ctx.UnidadNegocio })).ToList();
        var sinSaldo = new List<int>();
        foreach (var lote in candidatas.Chunk(100))
        {
            var pendientes = (await cn.QueryAsync<CargaComprobantesOrigen>("""
                SELECT DISTINCT LTRIM(RTRIM(TC)) Tc,LTRIM(RTRIM(IDCOMPROBANTE)) Comprobante
                FROM dbo.VT_V_MV_INSUMOS_SALDOSRM WHERE TC IN ('NP',' NP','  NP','RM',' RM','  RM')
                    AND LTRIM(RTRIM(TC)) IN ('NP','RM') AND IDCOMPROBANTE IN @Comprobantes AND SALDO>0;
                """, new { Comprobantes = lote.Select(x => x.Comprobante).ToArray() }))
                .Select(x => (x.Tc, x.Comprobante)).ToHashSet();
            sinSaldo.AddRange(lote.Where(x => !pendientes.Contains((x.Tc, x.Comprobante))).Select(x => x.Id));
            if (sinSaldo.Count > 0) break;
        }
        Assert.NotEmpty(sinSaldo); // Datos históricos detectados en la base de pruebas.
        foreach (var id in sinSaldo)
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => repo.GetOrigenAsync(cn, ctx, id, default));
        var remitos = await repo.BuscarAsync(cn, ctx, new() { TipoOrigen = "RM" }, default);
        Assert.All(remitos.Items, x => Assert.Equal("RM", x.Tc));
    }

    [CargaComprobantesSqlFact]
    public async Task FiltrosCombinadosRespetanFechasVendedorZonaYUnidad()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        var repo = new CargaComprobantesRepository();
        var ctx = await repo.GetContextoAsync(cn, Environment.GetEnvironmentVariable("ALFACORE_TEST_USUARIO") ?? "Administrador", default);
        Assert.NotEmpty(ctx.Vendedores); Assert.NotEmpty(ctx.UnidadesNegocio); Assert.NotEmpty(ctx.Depositos);
        var inicial = await repo.BuscarAsync(cn, ctx, new(), default);
        var doc = inicial.Items.First();
        var vendedor = await cn.ExecuteScalarAsync<string>("SELECT LTRIM(RTRIM(ISNULL(IdVendedor,''))) FROM dbo.V_MV_CPTE WHERE ID=@Id;", new { doc.Id });
        var zona = await cn.ExecuteScalarAsync<string>("SELECT TOP(1) LTRIM(RTRIM(ISNULL(ZONA,''))) FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CODIGO))=@Cuenta;", new { doc.Cuenta });
        var filtro = new CargaComprobantesFiltro
        {
            FechaDesde = doc.Fecha!.Value.Date, FechaHasta = doc.Fecha.Value.Date,
            Vendedor = vendedor ?? "", Zona = zona ?? "", UnidadNegocio = doc.UnidadNegocio, Deposito = doc.Deposito
        };
        var result = await repo.BuscarAsync(cn, ctx, filtro, default);
        Assert.Contains(result.Items, x => x.Id == doc.Id);
        Assert.All(result.Items, x =>
        {
            Assert.Equal(doc.Fecha.Value.Date, x.Fecha!.Value.Date);
            if (doc.UnidadNegocio.Length > 0) Assert.Equal(doc.UnidadNegocio, x.UnidadNegocio);
            if (doc.Deposito.Length > 0) Assert.Equal(doc.Deposito, x.Deposito);
        });
        if (filtro.Vendedor.Length > 0)
        {
            var reales = await cn.QueryAsync<string>("SELECT LTRIM(RTRIM(IdVendedor)) FROM dbo.V_MV_CPTE WHERE ID IN @Ids;", new { Ids = result.Items.Select(x => x.Id).ToArray() });
            Assert.All(reales, x => Assert.Equal(filtro.Vendedor, x));
        }
        if (filtro.Zona.Length > 0)
        {
            foreach (var row in result.Items)
            {
                var real = await cn.ExecuteScalarAsync<string>("SELECT TOP(1) LTRIM(RTRIM(ZONA)) FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CODIGO))=@Cuenta;", new { row.Cuenta });
                Assert.Equal(filtro.Zona, real);
            }
        }
        filtro.Zona = "__ZONA_INEXISTENTE__";
        Assert.Empty((await repo.BuscarAsync(cn, ctx, filtro, default)).Items);
        filtro.Zona = ""; filtro.Vendedor = "__VENDEDOR_INEXISTENTE__";
        Assert.Empty((await repo.BuscarAsync(cn, ctx, filtro, default)).Items);
        filtro.FechaDesde = new DateTime(2025, 1, 2); filtro.FechaHasta = new DateTime(2025, 1, 1);
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => repo.BuscarAsync(cn, ctx, filtro, default));
    }
}
