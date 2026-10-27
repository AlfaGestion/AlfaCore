using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed partial class CuentaCorrienteVentasTests
{
    [CargaComprobantesSqlFact]
    public async Task ComprasAperturaParcialesSaldosYExcelConSignoProveedor()
    {
        using var runtime=new CargaComprobantesArcaRuntime();var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var key=await cn.QuerySingleAsync<(string Cuenta,DateTime Hasta)>("""
            SELECT TOP 1 LTRIM(RTRIM(a.CUENTA)),MAX(a.FECHA) FROM dbo.MV_ASIENTOS a
            WHERE EsResumen=0 AND a.FECHA IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES p WHERE p.CTACODIGO=a.CUENTA)
            GROUP BY a.CUENTA HAVING COUNT(*)>5 AND SUM(CASE WHEN a.[DEBE-HABER]='H' THEN a.IMPORTE ELSE -a.IMPORTE END)<>0 ORDER BY COUNT(*) DESC;
            """);
        var fechas=(await cn.QueryAsync<DateTime>("SELECT DISTINCT FECHA FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(CUENTA))=@Cuenta AND EsResumen=0 AND FECHA IS NOT NULL ORDER BY FECHA",new { key.Cuenta })).ToList();
        var f=new FiltrosReporteCompras { Proveedor=key.Cuenta,FechaDesde=fechas[fechas.Count/2].Date,FechaHasta=key.Hasta.Date,TamanioPagina=1 };
        var expected=await cn.QuerySingleAsync<(decimal Apertura,decimal Cierre)>("""
            SELECT SUM(CASE WHEN FECHA<@Desde THEN CASE WHEN [DEBE-HABER]='H' THEN IMPORTE ELSE -IMPORTE END ELSE 0 END),
                SUM(CASE WHEN [DEBE-HABER]='H' THEN IMPORTE ELSE -IMPORTE END)
            FROM dbo.MV_ASIENTOS WHERE EsResumen=0 AND LTRIM(RTRIM(CUENTA))=@Cuenta AND FECHA<DATEADD(day,1,@Hasta)
            """,new { key.Cuenta,Desde=f.FechaDesde,Hasta=f.FechaHasta });
        var primera=await service.GetCuentaCorrienteAsync(false,f);Assert.Equal(expected.Apertura,primera.SaldoInicialTotal);Assert.Equal(expected.Cierre,primera.SaldoFinalTotal);
        f.Pagina=2;var segunda=await service.GetCuentaCorrienteAsync(false,f);
        Assert.Equal(primera.Items[0].SaldoParcial,segunda.Items[0].SaldoAnteriorLinea);
        Assert.Equal(segunda.Items[0].SaldoAnteriorLinea+segunda.Items[0].ImporteSignado,segunda.Items[0].SaldoParcial);
        Assert.Equal(segunda.Items[0].DebeHaber=="H"?segunda.Items[0].Importe:-segunda.Items[0].Importe,segunda.Items[0].ImporteSignado);
        var saldos=await service.GetSaldosComprasAsync(f);Assert.Equal(expected.Cierre,saldos.SaldoTotal);
        using(var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarCuentaCorriente(segunda.Items,f,segunda.SaldoTotal,false))))
        { Assert.Equal("Proveedor",wb.Worksheet(1).Cell(3,2).GetString());Assert.Equal("Saldo parcial",wb.Worksheet(1).Cell(3,8).GetString()); }
        using(var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarSaldosVentas(saldos.Items,f,false,false))))
            Assert.Equal("Proveedor",wb.Worksheet(1).Cell(3,2).GetString());
        f.Pagina=1;f.FechaDesde=key.Hasta.Date.AddDays(1);f.FechaHasta=f.FechaDesde;
        var quieta=await service.GetCuentaCorrienteAsync(false,f);var inicial=Assert.Single(quieta.Items);Assert.True(inicial.EsSaldoInicial);Assert.Equal(expected.Cierre,inicial.SaldoParcial);
        f.Proveedor=null;f.IncluirSaldosCero=true;
        Assert.Equal(await cn.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT LTRIM(RTRIM(CTACODIGO))) FROM dbo.VT_PROVEEDORES"),(await service.GetSaldosComprasAsync(f)).TotalRegistros);
        f.UnidadesNegocio=["__NO_EXISTE__"];f.IncluirSaldosCero=false;
        Assert.Empty((await service.GetSaldosComprasAsync(f)).Items);Assert.Empty((await service.GetCuentaCorrienteAsync(false,f)).Items);
        Assert.Empty((await service.GetDetalleAsync(false,f)).Items);
    }

    [CargaComprobantesSqlFact]
    public async Task ComprasResumidaYAnaliticaConcuerdanConFuenteVB6YPagosCreditos()
    {
        using var runtime=new CargaComprobantesArcaRuntime();var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var f=new FiltrosReporteCompras { FechaDesde=null,FechaHasta=null,TamanioPagina=1 };
        var r=await service.GetComprobantesCuentaCorrienteComprasAsync(f,false);
        var expected=await cn.QuerySingleAsync<(int Cantidad,decimal Pendiente)>("""
            SELECT COUNT(*),SUM(v.SALDO) FROM dbo.CO_CPTES_SALDOS_UNEG v WHERE v.SALDO>0
            AND EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES p WHERE p.CTACODIGO=v.CUENTA)
            """);
        Assert.Equal(expected.Cantidad,r.TotalRegistros);Assert.Equal(expected.Pendiente,r.PendienteTotal);Assert.True(Assert.Single(r.Items).Pendiente>0);
        f.Pagina=2;var segunda=await service.GetComprobantesCuentaCorrienteComprasAsync(f,false);Assert.NotEqual(r.Items[0].Orden,segunda.Items[0].Orden);
        var key=await cn.QuerySingleAsync<(string Cuenta,string Tc,string Id,DateTime Fecha)>("""
            SELECT TOP 1 LTRIM(RTRIM(v.CUENTA)),LTRIM(RTRIM(v.TC)),v.SUCURSAL+v.NUMERO+v.LETRA,v.FECHA FROM dbo.CO_CPTES_SALDOS_UNEG v
            WHERE v.SALDO=0 AND v.IMPORTE>0 AND EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES p WHERE p.CTACODIGO=v.CUENTA)
            AND EXISTS(SELECT 1 FROM dbo.MV_APLICACION a WHERE a.CUENTA=v.CUENTA AND a.TCO_ORIGEN=v.TC AND a.SUCURSAL_ORIGEN=v.SUCURSAL
                AND a.NUMERO_ORIGEN=v.NUMERO AND a.LETRA_ORIGEN=v.LETRA AND a.TC IN ('NCC','NCCP')) ORDER BY v.FECHA DESC;
            """);
        f=new() { Proveedor=key.Cuenta,TipoComprobante=key.Tc,FechaDesde=key.Fecha.Date,FechaHasta=key.Fecha.Date,TamanioPagina=500 };
        var analitico=await service.GetComprobantesCuentaCorrienteComprasAsync(f,true);var d=Assert.Single(analitico.Items,x=>x.IdComprobante==key.Id);
        Assert.Equal(0,d.Pendiente);Assert.Contains(d.Aplicaciones,x=>x.Tc is "NCC" or "NCCP");Assert.Equal(d.Aplicado,d.Aplicaciones.Sum(x=>x.Importe));Assert.Equal(0,d.DiferenciaAplicaciones);
        Assert.DoesNotContain((await service.GetComprobantesCuentaCorrienteComprasAsync(f,false)).Items,x=>x.IdComprobante==key.Id);
        using(var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarComprobantesCuentaCorriente(analitico.Items,f,true,false))))
        { Assert.Equal("Proveedor",wb.Worksheet("Comprobantes").Cell(3,2).GetString());Assert.Equal(2,wb.Worksheets.Count); }
        f.UnidadesNegocio=["__NO_EXISTE__"];Assert.Empty((await service.GetComprobantesCuentaCorrienteComprasAsync(f,true)).Items);
    }
}
