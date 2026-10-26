using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Xunit;

namespace AlfaCore.Tests;

public sealed partial class CuentaCorrienteVentasTests
{
    [CargaComprobantesSqlFact]
    public async Task SaldosConcilianConCuentaCorrienteYNoTruncanHistoriaNiPaginacion()
    {
        using var runtime=new CargaComprobantesArcaRuntime();var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var f=new FiltrosReporteCompras { FechaHasta=DateTime.Today,TamanioPagina=1 };
        var saldo=await service.GetSaldosVentasAsync(f,false);
        Assert.NotEmpty(saldo.Items);
        var cc=await service.GetCuentaCorrienteAsync(true,f);Assert.Equal(cc.SaldoFinalTotal,saldo.SaldoTotal);
        var esperado=await cn.ExecuteScalarAsync<decimal>("""
            SELECT ISNULL(SUM(CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE ELSE -a.IMPORTE END),0)
            FROM dbo.MV_ASIENTOS a WHERE UPPER(LTRIM(RTRIM(a.TC))) NOT IN ('FP','NCFP','CBFP') AND a.EsResumen=0 AND a.FECHA<DATEADD(day,1,@Hasta)
            AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA)))
            """,new { Hasta=f.FechaHasta });
        Assert.Equal(esperado,saldo.SaldoTotal);
        f.Pagina=2;var siguiente=await service.GetSaldosVentasAsync(f,false);
        Assert.Equal(saldo.SaldoTotal,siguiente.SaldoTotal);Assert.NotEqual(saldo.Items[0].Cuenta,siguiente.Items[0].Cuenta);
        f.Pagina=1;f.FechaDesde=DateTime.Today.AddYears(2);f.TipoComprobante="__NO_EXISTE__";
        Assert.Equal(saldo.SaldoTotal,(await service.GetSaldosVentasAsync(f,false)).SaldoTotal);
        f.IncluirSaldosCero=true;
        var todos=await service.GetSaldosVentasAsync(f,false);
        Assert.Equal(await cn.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT LTRIM(RTRIM(CTACODIGO))) FROM dbo.VT_CLIENTES"),todos.TotalRegistros);
        f.Proveedor=saldo.Items[0].Cuenta;f.UnidadesNegocio=["__NO_EXISTE__"];f.IncluirSaldosCero=false;
        Assert.Empty((await service.GetSaldosVentasAsync(f,false)).Items);
        f.IncluirSaldosCero=true;var vacio=await service.GetSaldosVentasAsync(f,false);
        Assert.All(vacio.Items,x=>Assert.Equal(0,x.Saldo));
        using var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarSaldosVentas(saldo.Items,f,false)));
        Assert.Equal("Saldo de la selección",wb.Worksheet(1).Cell(3,3).GetString());
    }

    [CargaComprobantesSqlFact]
    public async Task CreditoPruebaLimitesPositivosCeroYExcedidosConRollbackYDeudaGlobal()
    {
        using var runtime=new CargaComprobantesArcaRuntime();var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var cuenta=await cn.QuerySingleAsync<(string Cuenta,decimal Saldo)>("""
            SELECT TOP 1 LTRIM(RTRIM(a.CUENTA)),SUM(CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE ELSE -a.IMPORTE END)
            FROM dbo.MV_ASIENTOS a WHERE UPPER(LTRIM(RTRIM(a.TC))) NOT IN ('FP','NCFP','CBFP') AND a.EsResumen=0 AND a.FECHA<DATEADD(day,1,@Hasta)
            AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA)))
            GROUP BY a.CUENTA HAVING SUM(CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE ELSE -a.IMPORTE END)>100
            ORDER BY 2 DESC
            """,new { Hasta=DateTime.Today });
        var anterior=await cn.ExecuteScalarAsync<decimal?>("SELECT MAX(Limite_Credito) FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CTACODIGO))=@Cuenta",new { cuenta.Cuenta });
        await using(var tx=(Microsoft.Data.SqlClient.SqlTransaction)await cn.BeginTransactionAsync())
        {
            try
            {
                foreach(var limite in new[] { 0m,cuenta.Saldo+100m,cuenta.Saldo-100m })
                {
                    await cn.ExecuteAsync("UPDATE dbo.VT_CLIENTES SET Limite_Credito=@Limite WHERE LTRIM(RTRIM(CTACODIGO))=@Cuenta",new { cuenta.Cuenta,Limite=limite },tx);
                    var f=new FiltrosReporteCompras { FechaHasta=DateTime.Today,Proveedor=cuenta.Cuenta,
                        UnidadesNegocio=["__SIN_MOVIMIENTOS__"],IncluirSaldosCero=true };
                    var r=await ReporteComercialService.LeerSaldosVentasAsync(cn,f,true,transaction:tx);
                    var x=Assert.Single(r.Items);Assert.Equal(0,x.Saldo);Assert.Equal(cuenta.Saldo,x.SaldoGlobal);
                    Assert.Equal(limite==0?0:limite-cuenta.Saldo,x.CreditoDisponible);
                    Assert.Equal(limite>0 && limite<cuenta.Saldo?cuenta.Saldo-limite:0,x.Exceso);
                    using var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarSaldosVentas(r.Items,f,true)));
                    Assert.Equal("Disponible global",wb.Worksheet(1).Cell(3,6).GetString());Assert.Equal(x.CreditoDisponible,wb.Worksheet(1).Cell(4,6).GetValue<decimal>());
                }
            }
            finally { await tx.RollbackAsync(); }
        }
        Assert.Equal(anterior,await cn.ExecuteScalarAsync<decimal?>("SELECT MAX(Limite_Credito) FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CTACODIGO))=@Cuenta",new { cuenta.Cuenta }));
        var publico=await service.GetSaldosVentasAsync(new() { Proveedor=cuenta.Cuenta },true);
        Assert.NotEmpty(publico.Items);
    }
}
