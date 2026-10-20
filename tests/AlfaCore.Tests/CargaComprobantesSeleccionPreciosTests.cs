using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesSeleccionPreciosTests
{
    private static CargaComprobantesDirecta Draft() => new() { Cliente="112011541",UnidadNegocio="4",Deposito="1",
        Lineas=[new() { Articulo="403",Cantidad=2 }] };

    private static async Task<SqlConnection> AbrirAsync()
    {
        var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        Assert.Equal("SERVER-ALFAVB6",cn.DataSource.ToUpperInvariant());
        await cn.OpenAsync(); Assert.Equal("DISTRIWALTERP",cn.Database); return cn;
    }

    [Fact]
    public void OpcionesNulasConservanSnapshotAnteriorYMaestroSeDistingueDeHeredado()
    {
        var d=Draft(); var json=JsonSerializer.Serialize(d);
        Assert.DoesNotContain("ListaPrecio",json); Assert.DoesNotContain("ClasePrecio",json);
        d.ListaPrecio=""; d.ClasePrecio=5;
        var copia=JsonSerializer.Deserialize<CargaComprobantesDirecta>(JsonSerializer.Serialize(d))!;
        Assert.Equal("",copia.ListaPrecio); Assert.Equal(5,copia.ClasePrecio);
    }

    [CargaComprobantesSqlFact]
    public async Task SeleccionDeterminaPrecioYSeConservaEnCabeceraDetalleStockYAsiento()
    {
        await using var cn=await AbrirAsync(); using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            Assert.Equal(1,await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='SI' WHERE UPPER(LTRIM(RTRIM(CLAVE)))='V_MODIFICACPRECIO'",transaction:tx));
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET ModificaListaPrecios=1,CLASEPUSA='4,5' WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'",transaction:tx);
            Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MA_PRECIOS WHERE LTRIM(RTRIM(IdLista))='3' AND LTRIM(RTRIM(IdArticulo))='403' AND TipoLista='V'",transaction:tx));
            // Los triggers legacy también reportan filas afectadas; verificar el registro, no ExecuteAsync == 1.
            await cn.ExecuteAsync("UPDATE V_MA_PRECIOS SET Precio5=363 WHERE LTRIM(RTRIM(IdLista))='3' AND LTRIM(RTRIM(IdArticulo))='403' AND TipoLista='V'",transaction:tx);
            var ctx=await new CargaComprobantesRepository().GetContextoAsync(cn,"Administrador",default,tx);
            Assert.True(ctx.PuedeCambiarClase); Assert.True(ctx.PuedeCambiarLista);
            Assert.Equal(new[] { "4","5" },ctx.ClasesPrecio.Select(x=>x.Codigo));
            var draft=Draft(); draft.ListaPrecio="3"; draft.ClasePrecio=5;
            var planner=new CargaComprobantesPlanificador(new());
            var plan=await planner.CrearAsync(cn,ctx,0,10,[],DateTime.Today,default,tx,directa:draft);
            Assert.Equal(5,plan.ClasePrecio); Assert.Equal("3",plan.Origen.Lista);
            Assert.Equal(300m,plan.Origen.Lineas.Single().PrecioSinIva); Assert.Equal(726m,plan.Liquidacion.Total);
            var result=await new CargaComprobantesGrabador(planner,new()).GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default);
            Assert.Equal("3",await cn.ExecuteScalarAsync<string>("SELECT LTRIM(RTRIM(IdLista)) FROM V_MV_CPTE WHERE ID=@FacturaId",result,tx));
            Assert.Equal(5,await cn.ExecuteScalarAsync<int>("SELECT CLASEPRECIO FROM V_MV_CPTEINSUMOS WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",result,tx));
            Assert.Equal(5,await cn.ExecuteScalarAsync<int>("SELECT ClasePrecio FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante",result,tx));
            Assert.Equal(0m,await cn.ExecuteScalarAsync<decimal>("SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS WHERE TC='FC' AND SUCURSAL='1007' AND NUMERO='99999998' AND LETRA='A'",transaction:tx));
        }
        finally { if(tx.Connection is not null) tx.Rollback(); }
    }

    [CargaComprobantesSqlFact]
    public async Task RevocarPermisoDeListaInvalidaPlanAntesDeGrabarAunqueSeaAdministrador()
    {
        await using var cn=await AbrirAsync(); using var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            var ctx=await new CargaComprobantesRepository().GetContextoAsync(cn,"Administrador",default,tx,incluirCatalogos:false);
            var draft=Draft(); draft.ListaPrecio="3";
            var planner=new CargaComprobantesPlanificador(new());
            var plan=await planner.CrearAsync(cn,ctx,0,10,[],DateTime.Today,default,tx,directa:draft);
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET ModificaListaPrecios=0 WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'",transaction:tx);
            var antes=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>new CargaComprobantesGrabador(planner,new())
                .GrabarAsync(cn,tx,ctx,plan,Guid.NewGuid(),99999998,"00000000000000",DateTime.Today,default));
            Assert.Equal(antes,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE",transaction:tx));
        }
        finally { if(tx.Connection is not null) tx.Rollback(); }
    }

    [CargaComprobantesSqlFact]
    public async Task ClasesRespetanConfiguracionTokensExactosYNoAdmitenVentaAlCosto()
    {
        await using var cn=await AbrirAsync(); using var tx=cn.BeginTransaction();
        try
        {
            var resolver=new CargaComprobantesSeleccionPrecios(); var d=Draft(); d.ClasePrecio=5;
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='SI' WHERE UPPER(LTRIM(RTRIM(CLAVE)))='V_MODIFICACPRECIO'",transaction:tx);
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET CLASEPUSA='4,15' WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ResolverAsync(cn,"Administrador",d,default,tx));
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET CLASEPUSA='4,5' WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'",transaction:tx);
            Assert.Equal(5,(await resolver.ResolverAsync(cn,"Administrador",d,default,tx)).ClasePrecio);
            foreach(var valor in new[] { 0,9,-1 })
            { d.ClasePrecio=valor; await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ResolverAsync(cn,"Administrador",d,default,tx)); }
            d.ClasePrecio=5;
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='NO' WHERE UPPER(LTRIM(RTRIM(CLAVE)))='V_MODIFICACPRECIO'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ResolverAsync(cn,"Administrador",d,default,tx));
        }
        finally { if(tx.Connection is not null) tx.Rollback(); }
    }

    [CargaComprobantesSqlFact]
    public async Task ListaHeredaVigenciaObligatoriedadYPoliticaDePertenenciaSinFallbackSilencioso()
    {
        await using var cn=await AbrirAsync(); using var tx=cn.BeginTransaction();
        try
        {
            var resolver=new CargaComprobantesSeleccionPrecios(); var d=Draft();
            var pricing=await resolver.ResolverAsync(cn,"Administrador",d,default,tx);
            Assert.Equal("3",pricing.IdLista.Trim());
            await cn.ExecuteAsync("UPDATE V_MA_PRECIOSCAB SET VigenciaHasta=DATEADD(day,-1,GETDATE()) WHERE LTRIM(RTRIM(IdLista))='3' AND TipoLista='V'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ResolverAsync(cn,"Administrador",d,default,tx));
            var ctx=new CargaComprobantesContexto { Usuario="Administrador" };
            await resolver.CargarCatalogosAsync(cn,ctx,default,tx); Assert.DoesNotContain(ctx.ListasPrecio,x=>x.Codigo=="3");
            d.ListaPrecio="";
            Assert.False((await resolver.ResolverAsync(cn,"Administrador",d,default,tx)).UsaListas);
            await cn.ExecuteAsync("UPDATE TA_CONFIGURACION SET VALOR='SI' WHERE UPPER(LTRIM(RTRIM(CLAVE)))='LISTAOBLIGATORIO'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ResolverAsync(cn,"Administrador",d,default,tx));
            // La pertenencia se controla con independencia del precio positivo del maestro.
            await cn.ExecuteAsync("DELETE FROM V_MA_PRECIOS WHERE LTRIM(RTRIM(IdLista))='3' AND LTRIM(RTRIM(IdArticulo))='403' AND TipoLista='V'",transaction:tx);
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET ModificaListaPrecios=0 WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ValidarArticulosAsync(cn,"Administrador","FC",pricing,["403"],default,tx));
            await cn.ExecuteAsync("UPDATE TA_USUARIOS SET ModificaListaPrecios=1 WHERE UPPER(LTRIM(RTRIM(NOMBRE)))='ADMINISTRADOR'",transaction:tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ValidarArticulosAsync(cn,"Administrador","FC",pricing,["403"],default,tx));
            await resolver.ValidarArticulosAsync(cn,"Administrador","FP",pricing,["403"],default,tx);
        }
        finally { if(tx.Connection is not null) tx.Rollback(); }
    }

    [CargaComprobantesSqlFact]
    public async Task NotaCreditoNoPermiteSustituirPrecioHistoricoPorListaOClaseActual()
    {
        await using var cn=await AbrirAsync(); var resolver=new CargaComprobantesSeleccionPrecios(); var d=Draft(); d.Tc="NC";
        d.ListaPrecio="";
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ResolverAsync(cn,"Administrador",d,default));
        d.ListaPrecio=null; d.ClasePrecio=4;
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(()=>resolver.ResolverAsync(cn,"Administrador",d,default));
    }
}
