using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

// Pruebas de lectura en la base indicada explícitamente; no emiten ni modifican movimientos.
public sealed class ReportesCompatibilidadSqlFactAttribute : FactAttribute
{
    public ReportesCompatibilidadSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_REPORTES_SQL")))
            Skip = "Requiere ALFACORE_REPORTES_SQL para verificar consultas en SQL Server.";
    }
}

public sealed class ReportesCompatibilidadSqlTests
{
    private static SqlConnection Conexion() => new(Environment.GetEnvironmentVariable("ALFACORE_REPORTES_SQL"));

    private static ReporteComercialService Servicio()
    {
        var sesion = new SessionDto();
        T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler;
            return proxy;
        }
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch
        {
            "GetActiveSession" => sesion,
            "GetConnectionString" => Environment.GetEnvironmentVariable("ALFACORE_REPORTES_SQL"),
            _ => null
        });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch
        {
            "get_IsAuthenticated" or "IsAuthorizedForSession" => true,
            "GetCurrentUserName" => "PRUEBA_LECTURA_REPORTES",
            _ => null
        });
        var permissions = Proxy<IPermissionService>((_, _) => Task.FromResult(true));
        var events = Proxy<IAppEventService>((_, _) => Task.FromResult("PRUEBA_SQL"));
        return new(sessions, users, permissions, events);
    }

    [ReportesCompatibilidadSqlFact]
    public async Task ComprasDetallePaginadoConservaCantidadYTotalesDelResumen()
    {
        var svc = Servicio();
        var f = new FiltrosReporteCompras { FechaDesde = null, FechaHasta = null, TamanioPagina = 1 };
        var resumen = await svc.GetResumenAsync(false, f);
        var primera = await svc.GetDetalleAsync(false, f);
        Assert.True(primera.TotalRegistros > 1);
        Assert.Equal(resumen.CantidadComprobantes, primera.TotalRegistros);
        Assert.Single(primera.Items);
        f.Pagina = 2;
        var segunda = await svc.GetDetalleAsync(false, f);
        Assert.Single(segunda.Items);
        Assert.Equal(primera.TotalRegistros, segunda.TotalRegistros);
        f.UnidadesNegocio = ["__NO_EXISTE__"];
        Assert.Empty((await svc.GetDetalleAsync(false, f)).Items);
    }

    [ReportesCompatibilidadSqlFact]
    public async Task ComprasAcumuladoEntrePaginasYSaldosCoincidenConAsientos()
    {
        await using var cn = Conexion();
        await cn.OpenAsync();
        var cuenta = await cn.QuerySingleAsync<string>("""
            SELECT TOP (1) LTRIM(RTRIM(a.CUENTA)) FROM dbo.MV_ASIENTOS a
            WHERE a.EsResumen=0 AND a.FECHA IS NOT NULL
                AND EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES p WHERE p.CTACODIGO=a.CUENTA)
            GROUP BY a.CUENTA HAVING COUNT(*)>2 ORDER BY COUNT(*) DESC;
            """);
        var svc = Servicio();
        var f = new FiltrosReporteCompras { CuentaExacta = cuenta, FechaDesde = null, FechaHasta = null, TamanioPagina = 1 };
        var primera = await svc.GetCuentaCorrienteAsync(false, f);
        f.Pagina = 2;
        var segunda = await svc.GetCuentaCorrienteAsync(false, f);
        Assert.Equal(Assert.Single(primera.Items).SaldoParcial, Assert.Single(segunda.Items).SaldoAnteriorLinea);
        Assert.Equal(segunda.Items[0].SaldoAnteriorLinea + segunda.Items[0].ImporteSignado, segunda.Items[0].SaldoParcial);
        f.Pagina = 1;
        f.TamanioPagina = 500;
        var completa = await svc.GetCuentaCorrienteAsync(false, f);
        decimal acumulado = 0;
        foreach (var linea in completa.Items)
        {
            Assert.Equal(acumulado, linea.SaldoAnteriorLinea);
            acumulado += linea.ImporteSignado;
            Assert.Equal(acumulado, linea.SaldoParcial);
        }
        var saldos = await svc.GetSaldosComprasAsync(f);
        var esperado = await cn.ExecuteScalarAsync<decimal>("""
            SELECT ISNULL(SUM(CASE WHEN [DEBE-HABER]='H' THEN IMPORTE ELSE -IMPORTE END),0)
            FROM dbo.MV_ASIENTOS WHERE EsResumen=0 AND LTRIM(RTRIM(CUENTA))=@Cuenta
                AND FECHA<DATEADD(day,1,@Hasta);
            """, new { Cuenta = cuenta, Hasta = DateTime.Today });
        Assert.Equal(esperado, saldos.SaldoTotal);
        f.UnidadesNegocio = ["__NO_EXISTE__"];
        Assert.Empty((await svc.GetCuentaCorrienteAsync(false, f)).Items);
        Assert.Empty((await svc.GetSaldosComprasAsync(f)).Items);
    }

    [ReportesCompatibilidadSqlFact]
    public async Task ComprasResumidaAnaliticaYAcumuladoDeAplicaciones()
    {
        await using var cn = Conexion();
        await cn.OpenAsync();
        var svc = Servicio();
        var f = new FiltrosReporteCompras { FechaDesde = null, FechaHasta = null, TamanioPagina = 1 };
        var primera = await svc.GetComprobantesCuentaCorrienteComprasAsync(f, false);
        var esperado = await cn.QuerySingleAsync<(int Cantidad, decimal Saldo)>("""
            SELECT COUNT(*),ISNULL(SUM(v.SALDO),0) FROM dbo.CO_CPTES_SALDOS_UNEG v WHERE v.SALDO>0
                AND EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES p WHERE p.CTACODIGO=v.CUENTA);
            """);
        Assert.Equal(esperado.Cantidad, primera.TotalRegistros);
        Assert.Equal(esperado.Saldo, primera.PendienteTotal);
        Assert.True(Assert.Single(primera.Items).Pendiente > 0);
        f.Pagina = 2;
        Assert.NotEqual(primera.Items[0].Orden, Assert.Single((await svc.GetComprobantesCuentaCorrienteComprasAsync(f, false)).Items).Orden);
        var cuenta = await cn.QuerySingleAsync<string>("""
            SELECT TOP (1) LTRIM(RTRIM(v.CUENTA)) FROM dbo.CO_CPTES_SALDOS_UNEG v
            WHERE v.PAGO>0 AND EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES p WHERE p.CTACODIGO=v.CUENTA)
            GROUP BY v.CUENTA ORDER BY COUNT(*) DESC;
            """);
        f.CuentaExacta = cuenta; f.Pagina = 1; f.TamanioPagina = 500;
        var analitica = await svc.GetComprobantesCuentaCorrienteComprasAsync(f, true);
        Assert.Contains(analitica.Items, x => x.Aplicaciones.Count > 0);
        foreach (var documento in analitica.Items)
        {
            var saldo = documento.Importe;
            foreach (var aplicacion in documento.Aplicaciones)
            {
                saldo -= aplicacion.Importe;
                Assert.Equal(saldo, aplicacion.SaldoRestante);
            }
        }
        f.UnidadesNegocio = ["__NO_EXISTE__"];
        Assert.Empty((await svc.GetComprobantesCuentaCorrienteComprasAsync(f, true)).Items);
    }
}
