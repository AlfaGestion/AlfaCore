using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Reglas de «Clientes por mes»: qué meses se marcan sin facturar.</summary>
public sealed class ReporteClientesPorMesTests
{
    private static readonly IReadOnlyList<DateTime> Meses = ReporteComercialService.MesesDelPeriodo(new DateTime(2026, 6, 1), new DateTime(2026, 11, 2));
    private static readonly DateTime Hoy = new(2026, 11, 2);

    private static (string, string, int, int, decimal) V(string cuenta, int mes, decimal total) => (cuenta, "Cliente " + cuenta, 2026, mes, total);

    [Fact]
    public void AbonoMensualMarcaElMesQueFaltaYElMesEnCursoComoPendiente()
    {
        var r = ReporteComercialService.Armar(Meses, [V("A", 6, 100), V("A", 7, 100), V("A", 9, 100), V("A", 10, 100)],
            new Dictionary<string, (string, string)> { ["A"] = ("Cliente A", "MENSUAL") },
            new Dictionary<string, DateTime> { ["A"] = new(2025, 1, 10) }, Hoy);
        var f = Assert.Single(r.Filas);
        Assert.Equal([EstadoMesCliente.Facturado, EstadoMesCliente.Facturado, EstadoMesCliente.SinFacturar,
            EstadoMesCliente.Facturado, EstadoMesCliente.Facturado, EstadoMesCliente.Pendiente], f.Estados);
        Assert.Equal(1, f.MesesSinFacturar);
        Assert.Equal(400, f.Total);
    }

    [Fact]
    public void AbonoBimestralNoMarcaElMesQueNoLeToca()
    {
        var r = ReporteComercialService.Armar(Meses, [V("B", 5, 50), V("B", 7, 50), V("B", 9, 50)],
            new Dictionary<string, (string, string)> { ["B"] = ("Cliente B", "BIMESTRAL") },
            new Dictionary<string, DateTime> { ["B"] = new(2025, 1, 1) }, Hoy);
        // Junio queda cubierto por mayo (leído antes del período); noviembre (en curso) queda cubierto por... nada: octubre vacío.
        Assert.Equal([EstadoMesCliente.Vacio, EstadoMesCliente.Facturado, EstadoMesCliente.Vacio,
            EstadoMesCliente.Facturado, EstadoMesCliente.Vacio, EstadoMesCliente.Pendiente], Assert.Single(r.Filas).Estados);
    }

    [Fact]
    public void SinAbonoNoSeMarcaNadaYAbonoSinVentasApareceConTodoMarcado()
    {
        var r = ReporteComercialService.Armar(Meses, [V("C", 8, 300)],
            new Dictionary<string, (string, string)> { ["D"] = ("Cliente D", "MENSUAL") },
            new Dictionary<string, DateTime>(), Hoy);
        var c = r.Filas.Single(x => x.Cuenta == "C");
        Assert.Equal(0, c.MesesSinFacturar);
        Assert.False(c.FacturacionAutomatica);
        var d = r.Filas.Single(x => x.Cuenta == "D");
        Assert.Equal(5, d.MesesSinFacturar);
        Assert.Equal("Cliente D", d.RazonSocial);
        Assert.Equal("D", r.Filas[0].Cuenta); // primero los que tienen faltantes
    }

    [Fact]
    public void AntesDeLaPrimeraVentaNoHayAbonoQueControlar()
    {
        var r = ReporteComercialService.Armar(Meses, [V("E", 9, 10), V("E", 10, 10)],
            new Dictionary<string, (string, string)> { ["E"] = ("Cliente E", "MENSUAL") },
            new Dictionary<string, DateTime> { ["E"] = new(2026, 9, 5) }, Hoy);
        Assert.Equal(0, Assert.Single(r.Filas).MesesSinFacturar);
    }

    [Fact]
    public void NotaDeCreditoQueAnulaElMesCuentaComoSinFacturar()
    {
        var r = ReporteComercialService.Armar(Meses, [V("F", 6, 100), V("F", 7, 100), V("F", 7, -100), V("F", 8, 100), V("F", 9, 100), V("F", 10, 100)],
            new Dictionary<string, (string, string)> { ["F"] = ("Cliente F", "MENSUAL") },
            new Dictionary<string, DateTime> { ["F"] = new(2025, 1, 1) }, Hoy);
        Assert.Equal(EstadoMesCliente.SinFacturar, Assert.Single(r.Filas).Estados[1]);
    }
}

/// <summary>Ensayo explícito contra ALFANET2007_PRUEBAS (solo lectura). Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class ReporteClientesPorMesPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task UltimosDoceMesesEnPruebas()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Administrador'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        var svc = new ReporteComercialService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events), runtime.Events);

        var hoy = DateTime.Today;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await svc.GetClientesPorMesAsync(new FiltrosReporteCompras { FechaDesde = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-11), FechaHasta = hoy });
        output.WriteLine($"{sw.ElapsedMilliseconds} ms · {r.Filas.Count} clientes · {r.Filas.Count(f => f.FacturacionAutomatica)} con abono · {r.ClientesConFaltantes} con faltantes · total {r.Total:N2}");
        foreach (var f in r.Filas.Where(f => f.MesesSinFacturar > 0).Take(15))
            output.WriteLine($"{f.Cuenta} {f.RazonSocial} [{f.Periodicidad}] faltan {f.MesesSinFacturar}: " +
                string.Join(" ", r.Meses.Select((m, i) => $"{m:MM/yy}={(f.Estados[i] == EstadoMesCliente.SinFacturar ? "X" : f.Estados[i] == EstadoMesCliente.Pendiente ? "P" : f.Importes[i].ToString("0"))}")));
        Assert.Equal(12, r.Meses.Count);
        Assert.Equal(r.Total, r.TotalesPorMes.Sum());
        Assert.NotEmpty(new ReporteComprasExcelExporter().ExportarClientesPorMes(r, new FiltrosReporteCompras()));
    }
}
