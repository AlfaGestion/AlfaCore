using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Asientos sin período («Todo»): lista los últimos cargados contra una base real (PLAN_CUENTAS_TEST_SQL). Solo lectura.
/// El usuario se toma de PLAN_CUENTAS_TEST_USUARIO (por defecto Marcela) porque el servicio lo busca en TA_USUARIOS.
/// </summary>
public class AsientosUltimosSqlTests(ITestOutputHelper output)
{
    [PlanCuentasSqlFact]
    public async Task SinPeriodoTraeLosUltimosAsientos()
    {
        var connection = Environment.GetEnvironmentVariable("PLAN_CUENTAS_TEST_SQL")!;
        var usuario = Environment.GetEnvironmentVariable("PLAN_CUENTAS_TEST_USUARIO") is { Length: > 0 } u ? u : "Marcela";
        var service = new AsientosService(new PlanCuentasAbmSqlTests.Sesion(connection), new PlanCuentasAbmSqlTests.Usuario(usuario),
            new PlanCuentasAbmSqlTests.Permisos(), new PlanCuentasAbmSqlTests.Eventos());

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var manuales = await service.BuscarAsync(new AsientosFiltro { Origen = "MANUALES" });
        output.WriteLine($"Manuales: {manuales.Count} en {reloj.ElapsedMilliseconds} ms; primero {manuales.FirstOrDefault()?.Fecha:dd/MM/yyyy}");
        Assert.NotEmpty(manuales);
        Assert.True(manuales.Count <= AsientosService.UltimosAsientos);
        Assert.All(manuales, x => Assert.True(x.Manual));
        Assert.Equal(manuales.OrderByDescending(x => x.Fecha).Select(x => x.Fecha), manuales.Select(x => x.Fecha));

        reloj.Restart();
        var todos = await service.BuscarAsync(new AsientosFiltro { Origen = "TODOS" });
        output.WriteLine($"Todos: {todos.Count} en {reloj.ElapsedMilliseconds} ms");
        Assert.Equal(AsientosService.UltimosAsientos, todos.Count);

        // Con período elegido sigue el tope de un año.
        await Assert.ThrowsAnyAsync<Exception>(() => service.BuscarAsync(new AsientosFiltro { Desde = DateTime.Today.AddYears(-2), Hasta = DateTime.Today }));
    }
}
