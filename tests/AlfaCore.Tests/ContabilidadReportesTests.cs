using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Balance de sumas y saldos: títulos por prefijo de código y clientes/proveedores agrupados.</summary>
public sealed class ContabilidadReportesTests
{
    private static readonly (string, string, bool, string)[] Plan =
    [
        ("1", "ACTIVO", true, ""), ("11", "ACTIVO CORRIENTE", true, ""), ("11201", "DEUDORES POR VENTAS", true, ""),
        ("112010001", "Cliente A", false, "CL"), ("112010002", "Cliente B", false, "CL"),
        ("11101", "CAJA", true, ""), ("111010001", "Caja pesos", false, ""), ("4", "VENTAS", true, ""), ("43101", "Ventas", false, "")
    ];

    private static readonly (string, decimal, decimal, decimal)[] Saldos =
    [
        ("112010001", 100, 50, 30), ("112010002", 0, 20, 0), ("111010001", 10, 30, 0), ("43101", 0, 0, 100)
    ];

    [Fact]
    public void TitulosSumanSusCuentasYClientesSeAgrupan()
    {
        var filas = ContabilidadReportesService.ArmarSumasSaldos(Plan, Saldos, agruparTerceros: true, mostrarTitulos: true, sinMovimientos: false);
        var clientes = Assert.Single(filas, f => f.Agrupadas > 0);
        Assert.Equal(2, clientes.Agrupadas);
        Assert.Equal(140, clientes.Saldo);                      // 100+50-30 + 20
        var activo = Assert.Single(filas, f => f.Codigo == "1" && f.EsTitulo);
        Assert.Equal(180, activo.Saldo);                        // clientes 140 + caja 40
        Assert.Equal(-100, Assert.Single(filas, f => f.Codigo == "4" && f.EsTitulo).Saldo);
        // El título va antes que sus cuentas.
        Assert.True(filas.FindIndex(f => f.Codigo == "11201" && f.EsTitulo) < filas.IndexOf(clientes));
    }

    [Fact]
    public void SinAgruparMuestraCadaClienteYSinTitulosSoloImputables()
    {
        var filas = ContabilidadReportesService.ArmarSumasSaldos(Plan, Saldos, agruparTerceros: false, mostrarTitulos: false, sinMovimientos: false);
        Assert.Equal(4, filas.Count);
        Assert.All(filas, f => Assert.False(f.EsTitulo));
        Assert.Equal(filas.Select(f => f.Codigo).Order(StringComparer.Ordinal), filas.Select(f => f.Codigo));
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS (solo lectura). Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class ContabilidadReportesPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task BalanceCuadraConLosAsientosYElMayorConElBalance()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Alberto'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var svc = new ContabilidadReportesService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events),
            runtime.Events, provider.GetRequiredService<DocumentPdfService>());

        var cat = await svc.GetCatalogosAsync();
        var ej = cat.Ejercicios.First(e => DateTime.Today >= e.Desde && DateTime.Today <= e.Hasta);
        var filtro = new ContabilidadFiltro { Periodo = ej.Periodo, Desde = new DateTime(ej.Desde.Year, 7, 1), Hasta = DateTime.Today };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var b = await svc.GetSumasSaldosAsync(filtro);
        output.WriteLine($"{ej.Descripcion}: {b.Filas.Count} filas en {sw.ElapsedMilliseconds} ms · anterior {b.TotalSaldoAnterior:N2} · debe {b.TotalDebe:N2} · haber {b.TotalHaber:N2} · desbalanceados {b.Desbalanceados.Count} ({b.Desbalanceados.Sum(a => a.Diferencia):N2})");
        foreach (var f in b.Filas.Where(f => f.Nivel <= 1 && f.EsTitulo)) output.WriteLine($"  {f.Codigo} {f.Descripcion}: {f.Saldo:N2}");
        var esperado = await cn.QuerySingleAsync<(decimal Debe, decimal Haber)>("""
            SELECT ISNULL(SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE 0 END),0), ISNULL(SUM(CASE WHEN [DEBE-HABER]<>'D' THEN IMPORTE ELSE 0 END),0)
            FROM dbo.MV_ASIENTOS WHERE PERIODO=@P AND FECHA>=@D AND FECHA<DATEADD(day,1,@H) AND LTRIM(RTRIM(ISNULL(TC,''))) NOT IN ('FP','NCFP','NDFP','CBFP','FPC','NCCP','PGFP')
            """, new { P = ej.Periodo, D = filtro.Desde, H = filtro.Hasta });
        Assert.Equal(esperado.Debe, b.TotalDebe);
        Assert.Equal(esperado.Haber, b.TotalHaber);
        Assert.Equal(b.TotalDebe - b.TotalHaber, b.Desbalanceados.Sum(a => a.Diferencia), 2);

        // El mayor de la cuenta imputable con más movimiento termina en el saldo de su renglón.
        var cuenta = b.Filas.Where(f => !f.EsTitulo && f.Agrupadas == 0).OrderByDescending(f => f.Debe + f.Haber).First();
        var m = await svc.GetMayorAsync(new ContabilidadFiltro { Periodo = ej.Periodo, Desde = filtro.Desde, Hasta = filtro.Hasta, CuentaDesde = cuenta.Codigo });
        var c = Assert.Single(m.Cuentas);
        output.WriteLine($"Mayor {c.Codigo} {c.Descripcion}: anterior {c.SaldoAnterior:N2} · {c.Movimientos.Count} mov · saldo {c.Saldo:N2} (balance {cuenta.Saldo:N2})");
        Assert.Equal(cuenta.Saldo, c.Saldo);
        Assert.Equal(c.Saldo, c.Movimientos.Last().Saldo);

        // Mayor de un título: todas sus cuentas.
        var titulo = b.Filas.First(f => f.EsTitulo && f.Codigo.Length == 5);
        var mt = await svc.GetMayorAsync(new ContabilidadFiltro { Periodo = ej.Periodo, Desde = filtro.Desde, Hasta = filtro.Hasta, CuentaDesde = titulo.Codigo });
        output.WriteLine($"Mayor del título {titulo.Codigo}: {mt.Cuentas.Count} cuentas, {mt.Movimientos} movimientos, saldo {mt.Cuentas.Sum(x => x.Saldo):N2} (balance {titulo.Saldo:N2})");
        Assert.Equal(titulo.Saldo, mt.Cuentas.Sum(x => x.Saldo));

        Assert.True((await svc.ExportarSumasSaldosAsync(filtro, false)).Contenido.Length > 1000);
        Assert.True((await svc.ExportarMayorAsync(new ContabilidadFiltro { Periodo = ej.Periodo, Desde = filtro.Desde, Hasta = filtro.Hasta, CuentaDesde = cuenta.Codigo }, true)).Contenido.Length > 1000);
    }
}
