using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed partial class CuentaCorrienteVentasTests
{
    [CargaComprobantesSqlFact]
    public async Task AperturaAcumuladosPaginacionYExcelConcuerdanConAsientosSinModificarLaBase()
    {
        using var runtime=new CargaComprobantesArcaRuntime();
        var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas(); await cn.OpenAsync();
        var cuenta=await cn.QuerySingleAsync<(string Cuenta,DateTime Fecha)>("""
            SELECT TOP 1 LTRIM(RTRIM(a.CUENTA)) Cuenta,MAX(a.FECHA) Fecha
            FROM dbo.MV_ASIENTOS a WHERE UPPER(LTRIM(RTRIM(a.TC))) NOT IN ('FP','NCFP','CBFP') AND a.EsResumen=0 AND a.FECHA IS NOT NULL
            AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA)))
            GROUP BY a.CUENTA HAVING COUNT(*)>5 ORDER BY COUNT(*) DESC;
            """);
        var fechas=(await cn.QueryAsync<DateTime>("SELECT DISTINCT FECHA FROM dbo.MV_ASIENTOS WHERE UPPER(LTRIM(RTRIM(TC))) NOT IN ('FP','NCFP','CBFP') AND LTRIM(RTRIM(CUENTA))=@Cuenta AND EsResumen=0 AND FECHA IS NOT NULL ORDER BY FECHA",new { cuenta.Cuenta })).ToList();
        var desde=fechas[fechas.Count/2].Date;
        var f=new FiltrosReporteCompras { Proveedor=cuenta.Cuenta,FechaDesde=desde,FechaHasta=cuenta.Fecha.Date,TamanioPagina=1 };
        var primera=await service.GetCuentaCorrienteAsync(true,f);
        Assert.Single(primera.Items); Assert.True(primera.TotalRegistros>1);
        var esperado=await cn.QuerySingleAsync<(decimal Apertura,decimal Movimiento,decimal Cierre)>("""
            SELECT ISNULL(SUM(CASE WHEN FECHA<@Desde THEN CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END ELSE 0 END),0),
                ISNULL(SUM(CASE WHEN FECHA>=@Desde THEN CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END ELSE 0 END),0),
                ISNULL(SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END),0)
            FROM dbo.MV_ASIENTOS WHERE UPPER(LTRIM(RTRIM(TC))) NOT IN ('FP','NCFP','CBFP') AND LTRIM(RTRIM(CUENTA))=@Cuenta AND EsResumen=0 AND FECHA<DATEADD(day,1,@Hasta)
            """,new { cuenta.Cuenta,Desde=desde,Hasta=cuenta.Fecha.Date });
        Assert.Equal(esperado.Apertura,primera.SaldoInicialTotal);
        Assert.Equal(esperado.Movimiento,primera.SaldoTotal);Assert.Equal(esperado.Cierre,primera.SaldoFinalTotal);
        f.Pagina=2;var segunda=await service.GetCuentaCorrienteAsync(true,f);
        Assert.Equal(primera.Items[0].SaldoParcial,segunda.Items[0].SaldoAnteriorLinea);
        Assert.Equal(segunda.Items[0].SaldoAnteriorLinea+segunda.Items[0].ImporteSignado,segunda.Items[0].SaldoParcial);
        f.Pagina=1;f.TamanioPagina=500;
        var items=new List<CuentaCorrienteFilaDto>();
        do { var pagina=await service.GetCuentaCorrienteAsync(true,f);items.AddRange(pagina.Items);if(items.Count>=pagina.TotalRegistros)break;f.Pagina++; } while(true);
        var parcial=esperado.Apertura;
        foreach(var item in items)
        {
            Assert.Equal(cuenta.Cuenta,item.Cuenta);Assert.Equal(parcial,item.SaldoAnteriorLinea);
            parcial+=item.ImporteSignado;Assert.Equal(parcial,item.SaldoParcial);
        }
        Assert.Equal(esperado.Cierre,parcial);Assert.True(items.Last().UltimaLineaCuenta);
        using var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarCuentaCorriente(items,f,primera.SaldoTotal,true)));
        var ws=wb.Worksheet(1);Assert.Equal("Saldo parcial",ws.Cell(3,8).GetString());
        Assert.Equal("Saldo inicial",ws.Cell(4,9).GetString());Assert.Equal(esperado.Apertura,ws.Cell(4,8).GetValue<decimal>());
        var ultima=ws.LastRowUsed()!.RowNumber();Assert.Equal("Saldo al cierre",ws.Cell(ultima,9).GetString());
        Assert.Equal(esperado.Cierre,ws.Cell(ultima,8).GetValue<decimal>());

        f.Pagina=1; f.FechaDesde=cuenta.Fecha.Date.AddDays(1);f.FechaHasta=f.FechaDesde;
        var quieta=await service.GetCuentaCorrienteAsync(true,f);
        if(esperado.Cierre!=0) { var saldo=Assert.Single(quieta.Items);Assert.True(saldo.EsSaldoInicial);Assert.Equal(esperado.Cierre,saldo.SaldoParcial); }
        else Assert.Empty(quieta.Items);

        // Un filtro visual por TC conserva el saldo contable, con los pagos de otros tipos.
        f.FechaDesde=desde;f.FechaHasta=cuenta.Fecha.Date;f.TipoComprobante=items[0].Tc;
        var filtrada=await service.GetCuentaCorrienteAsync(true,f);
        Assert.All(filtrada.Items,x=>Assert.Equal(f.TipoComprobante,x.Tc));
        Assert.Equal(primera.SaldoFinalTotal,filtrada.SaldoFinalTotal);
        foreach(var x in filtrada.Items) Assert.Contains(items,y=>y.Fecha==x.Fecha && y.Tc==x.Tc && y.IdComprobante==x.IdComprobante && y.SaldoParcial==x.SaldoParcial);
    }

    [CargaComprobantesSqlFact]
    public async Task CarteraYUnidadFiltranAperturaYLibrosSegunMaestroDelCliente()
    {
        using var runtime=new CargaComprobantesArcaRuntime();var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var cartera=await cn.QuerySingleAsync<(string Vendedor,string Zona,string Unidad)>("""
            SELECT TOP 1 ISNULL(LTRIM(RTRIM(p.IdVendedor)),'') Vendedor,ISNULL(LTRIM(RTRIM(p.ZONA)),'') Zona,
                ISNULL(LTRIM(RTRIM(a.UNEGOCIO)),'') Unidad
            FROM dbo.MV_ASIENTOS a JOIN dbo.VT_CLIENTES p ON LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA))
            WHERE a.EsResumen=0 AND a.FECHA IS NOT NULL ORDER BY a.FECHA DESC;
            """);
        var f=new FiltrosReporteCompras { FechaDesde=null,FechaHasta=null,TamanioPagina=500,
            Vendedor=cartera.Vendedor==""?null:cartera.Vendedor,SinVendedor=cartera.Vendedor=="",
            Zona=cartera.Zona==""?null:cartera.Zona,SinZona=cartera.Zona=="",UnidadesNegocio=[cartera.Unidad] };
        var cc=await service.GetCuentaCorrienteAsync(true,f);Assert.NotEmpty(cc.Items);
        var esperado=await cn.ExecuteScalarAsync<decimal>("""
            SELECT ISNULL(SUM(CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE ELSE -a.IMPORTE END),0)
            FROM dbo.MV_ASIENTOS a WHERE UPPER(LTRIM(RTRIM(a.TC))) NOT IN ('FP','NCFP','CBFP') AND a.EsResumen=0 AND a.FECHA IS NOT NULL AND ISNULL(LTRIM(RTRIM(a.UNEGOCIO)),'')=@Unidad
            AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA))
                AND ISNULL(LTRIM(RTRIM(p.IdVendedor)),'')=@Vendedor AND ISNULL(LTRIM(RTRIM(p.ZONA)),'')=@Zona)
            """,new { cartera.Unidad,cartera.Vendedor,cartera.Zona });
        Assert.Equal(esperado,cc.SaldoFinalTotal);Assert.Equal(0,cc.SaldoInicialTotal);
        var detalle=await service.GetDetalleAsync(true,f);
        var cantidad=await cn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Libro_VentasConFP a WHERE UPPER(LTRIM(RTRIM(a.TC))) NOT IN ('FP','NCFP','CBFP') AND ISNULL(LTRIM(RTRIM(a.UNEGOCIO)),'')=@Unidad
            AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA))
                AND ISNULL(LTRIM(RTRIM(p.IdVendedor)),'')=@Vendedor AND ISNULL(LTRIM(RTRIM(p.ZONA)),'')=@Zona)
            """,new { cartera.Unidad,cartera.Vendedor,cartera.Zona });
        Assert.Equal(cantidad,detalle.TotalRegistros);
        f.FechaHasta=DateTime.Today;
        var saldosCartera=await service.GetSaldosVentasAsync(f,false);
        Assert.Equal((await service.GetCuentaCorrienteAsync(true,f)).SaldoFinalTotal,saldosCartera.SaldoTotal);
        f.Vendedor="__SIN_COINCIDENCIAS__";f.SinVendedor=false;
        Assert.Empty((await service.GetSaldosVentasAsync(f,true)).Items);
        Assert.Empty((await service.GetCuentaCorrienteAsync(true,f)).Items);
        Assert.Empty((await service.GetDetalleAsync(true,f)).Items);
    }

    private static SqlConnection ConexionPruebas()
    {
        var cs=Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL");var b=new SqlConnectionStringBuilder(cs);
        Assert.Equal("SERVER-ALFAVB6",b.DataSource,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("DISTRIWALTERP",b.InitialCatalog,StringComparer.OrdinalIgnoreCase);
        return new SqlConnection(cs);
    }

    private static ReporteComercialService CrearServicio(CargaComprobantesArcaRuntime runtime,string usuario="Administrador")
    {
        var sesion=new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler=(m,_)=>m.Name switch
        { "GetActiveSession"=>sesion,"GetConnectionString"=>Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"),_=>null };
        var users=DispatchProxy.Create<IAppUserSessionService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler=(m,_)=>m.Name switch
        { "get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>usuario,_=>null };
        var permisos=DispatchProxy.Create<IPermissionService,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)permisos).Handler=(_,_)=>Task.FromResult(true);
        return new ReporteComercialService(runtime.Sessions,users,permisos,runtime.Events);
    }
}
