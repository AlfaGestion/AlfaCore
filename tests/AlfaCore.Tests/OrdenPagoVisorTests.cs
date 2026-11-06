using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Visor de órdenes de pago: listado y detalle (trazabilidad, asiento, historial) sobre la base de pruebas.</summary>
[Collection("Conciliacion SQL")]
public sealed class OrdenPagoVisorTests(ITestOutputHelper output)
{
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler; return proxy;
    }

    private static (OrdenPagoService Service, string Connection) Servicio(string usuario)
    {
        var connection = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")!;
        using var runtime = new CargaComprobantesArcaRuntime(); var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch { "GetConnectionString" => connection, "GetActiveSession" => sesion, _ => null });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => usuario, _ => null });
        return (new OrdenPagoService(sessions, users, Proxy<IPermissionService>((_, _) => Task.FromResult(true)), runtime.Events), connection);
    }

    [Fact]
    public void LosEstadosYLosNombresDelHistorialSeEntienden()
    {
        Assert.Equal("Anulada", new OrdenPagoFila { Anulada = true }.Estado);
        Assert.Equal("Pendiente de liquidar", new OrdenPagoFila { Pendiente = true }.Estado);
        Assert.Equal("Pagada", new OrdenPagoFila().Estado);
        Assert.Equal(150m, new OrdenPagoCancelado { PorOrden = 100, PorCreditos = 50 }.Total);
        Assert.Equal("Alta", ComprobanteAcciones.Nombre("A"));
        Assert.Equal("Alta", ComprobanteAcciones.Nombre("IN"));
        Assert.Equal("Modificación", ComprobanteAcciones.Nombre("M"));
        Assert.Equal("Baja", ComprobanteAcciones.Nombre("B"));
        Assert.Equal("Impresión / PDF", ComprobanteAcciones.Nombre(ComprobanteAcciones.Impresion));
        Assert.Equal("ER", ComprobanteAcciones.Nombre("ER"));
    }

    [OrdenPagoSqlFact]
    public async Task ListaLasOrdenesYMuestraElDetalleConTrazabilidadEHistorial()
    {
        var (service, connection) = Servicio("Administrador");
        var desde = new DateTime(2020, 1, 1); var hasta = DateTime.Today;
        var todas = await service.ConsultarAsync(desde, hasta, "", "", "", CancellationToken.None);
        Assert.NotEmpty(todas);
        Assert.All(todas, f => Assert.Contains(f.Tc, new[] { "OPG", "PGFP" }));
        Assert.All(await service.ConsultarAsync(desde, hasta, "", "PGFP", "ACTIVAS"), f => { Assert.Equal("PGFP", f.Tc); Assert.False(f.Anulada); });
        var ejemplo = todas.First();
        Assert.NotEmpty(await service.ConsultarAsync(desde, hasta, ejemplo.Nombre[..Math.Min(5, ejemplo.Nombre.Length)], "", ""));

        // Una orden grabada con el servicio, con asiento y aplicaciones: el detalle explica qué pagó y con qué.
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        var idsWeb = (await cn.QueryAsync<(string Tc, string Id)>("""
            SELECT LTRIM(RTRIM(Tc)), Comprobante FROM dbo.ALFACORE_PAGO_OPERACION ORDER BY FechaHora DESC
            """)).ToList();
        Assert.NotEmpty(idsWeb);
        foreach (var (tc, id) in idsWeb.Take(6))
        {
            var d = await service.DetalleAsync(tc, id);
            Assert.NotNull(d);
            Assert.True(d!.Cabecera.Web);
            Assert.NotEmpty(d.Cancelados);
            Assert.NotEmpty(d.Medios);
            Assert.Equal(d.Cabecera.Importe, d.Medios.Sum(m => m.Importe) + d.RetencionGanancias);   // lo cancelado = medios + retención
            Assert.Equal(d.Asiento.Where(l => l.DebeHaber.Trim() == "D").Sum(l => l.Importe), d.Asiento.Where(l => l.DebeHaber.Trim() == "H").Sum(l => l.Importe));
            Assert.Equal(d.Cancelados.Sum(c => c.Total), d.Cancelados.Sum(c => c.PorOrden) + d.CreditosUsados.Sum(c => c.Importe));
            output.WriteLine($"{tc} {id}: {d.Cancelados.Count} cancelados, {d.CreditosUsados.Count} créditos, {d.Medios.Count} medios, saldo a favor {d.SaldoAFavor:N2}, {d.Historial.Count} eventos");
        }
        Assert.Null(await service.DetalleAsync("PG", "000100000223X"));       // solo OPG y PGFP
        Assert.Null(await service.DetalleAsync("OPG", "000999999999X"));
    }

    [OrdenPagoSqlFact]
    public async Task ElDetalleCuentaLosCreditosAplicadosYElHistorialIncluyeLosMails()
    {
        var (service, connection) = Servicio("Administrador");
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        // Una orden pagada con crédito (NC) y una con mails enviados desde el legacy.
        var conCredito = await cn.QueryFirstOrDefaultAsync<(string Tc, string Id)?>("""
            SELECT TOP 1 LTRIM(RTRIM(TC_PRINT)), IDCOMPROBANTE_PRINT FROM dbo.MV_APLICACION
            WHERE LTRIM(RTRIM(TC_PRINT)) IN ('OPG','PGFP') AND LTRIM(RTRIM(TC)) NOT IN ('OPG','PGFP') AND IDCOMPROBANTE_PRINT IS NOT NULL ORDER BY IDCOMPROBANTE_PRINT DESC
            """);
        if (conCredito is { } c)
        {
            var d = await service.DetalleAsync(c.Tc, c.Id);
            Assert.NotNull(d);
            Assert.NotEmpty(d!.CreditosUsados);
            Assert.Contains(d.Cancelados, x => x.PorCreditos > 0);
        }
        var conMail = await cn.QueryFirstOrDefaultAsync<(string Tc, string Id)?>("""
            SELECT TOP 1 LTRIM(RTRIM(m.TC)), LTRIM(RTRIM(m.IdComprobante)) FROM dbo.V_MA_EMAILMOV m JOIN dbo.V_MV_CPTE c ON c.TC=m.TC AND c.IDCOMPROBANTE=m.IdComprobante
            WHERE LTRIM(RTRIM(m.TC)) IN ('OPG','PGFP') AND ISNULL(c.ANULADA,0)=0 ORDER BY m.FechaHora DESC
            """);
        if (conMail is { } m)
        {
            var d = await service.DetalleAsync(m.Tc, m.Id);
            Assert.NotNull(d);
            Assert.Contains(d!.Historial, e => e.Tipo == "Mail" && e.Detalle.StartsWith("Enviado a"));
            Assert.Equal(d.Historial.OrderByDescending(e => e.Fecha).Select(e => e.Fecha), d.Historial.Select(e => e.Fecha));
        }
    }
}
