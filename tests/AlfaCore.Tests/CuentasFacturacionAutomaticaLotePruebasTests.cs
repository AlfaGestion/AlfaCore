using System.Reflection;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Ensayo explícito contra ALFANET2007_PRUEBAS de las acciones por lote de facturación automática:
/// agrega un concepto de prueba (por porcentaje del abono), lo lista y lo quita. Deja la base como estaba.
/// Requiere ALFACORE_TEST_SQL hacia ALFANET2007_PRUEBAS y ALFACORE_FA_LOTE=cuenta[,cuenta].
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class CuentasFacturacionAutomaticaLotePruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaLoteFact]
    public async Task AgregaListaYQuitaUnConceptoDePrueba()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var validator = (ICuentasComercialesValidator)Activator.CreateInstance(typeof(CuentasComercialesValidator),
            typeof(CuentasComercialesValidator).GetConstructors()[0].GetParameters().Select(_ => (object?)null).ToArray())!;
        var service = new CuentasComercialesService(runtime.Configuration, runtime.Sessions, runtime.Events, validator);
        var cuentas = Environment.GetEnvironmentVariable("ALFACORE_FA_LOTE")!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        const string detalle = "PRUEBA LOTE ALFACORE <PERIODO>";

        var antes = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MA_CUENTAS_AUTOCPTESDET WHERE Cuenta IN @c", new { c = cuentas });
        var agregado = await service.AgregarConceptoFacturacionAutomaticaAsync(cuentas, detalle, 10m, esPorcentaje: true, cuota: 1, cuotas: 2);
        output.WriteLine($"Agregado: {agregado.Renglones} renglón(es) en {agregado.Clientes} cliente(s); no modificados: {string.Join(",", agregado.NoModificados)}");
        foreach (var r in await cn.QueryAsync("SELECT Cuenta, SucursalCuenta, Importe, NroUltimaCuota, Cuotas FROM MA_CUENTAS_AUTOCPTESDET WHERE Cuenta IN @c AND Detalle=@d", new { c = cuentas, d = detalle }))
            output.WriteLine($"   {r.Cuenta} suc {r.SucursalCuenta}: {r.Importe:N2} · cuota {r.NroUltimaCuota} de {r.Cuotas}");

        var repetido = await service.AgregarConceptoFacturacionAutomaticaAsync(cuentas, detalle, 10m, esPorcentaje: true, cuota: 1, cuotas: 2);
        Assert.Equal(0, repetido.Renglones); // no duplica un detalle que ya tiene

        var conceptos = await service.GetConceptosFacturacionAutomaticaAsync(cuentas);
        foreach (var c in conceptos) output.WriteLine($"Concepto: {c.Detalle} · {c.Clientes} cliente(s) · {c.Renglones} renglón(es) · {c.Cumplidos} cumplidos");
        Assert.Contains(conceptos, c => c.Detalle == detalle);

        var quitado = await service.QuitarConceptoFacturacionAutomaticaAsync(cuentas, detalle, soloCumplidos: false);
        output.WriteLine($"Quitado: {quitado.Renglones} renglón(es) de {quitado.Clientes} cliente(s)");
        Assert.Equal(agregado.Renglones, quitado.Renglones);
        Assert.Equal(antes, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MA_CUENTAS_AUTOCPTESDET WHERE Cuenta IN @c", new { c = cuentas }));
    }
}

public sealed class FacturacionAutomaticaLoteFactAttribute : FactAttribute
{
    public FacturacionAutomaticaLoteFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_FA_LOTE")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")))
            Skip = "Ensayo explícito: requiere ALFACORE_FA_LOTE=cuenta[,cuenta] y ALFACORE_TEST_SQL hacia ALFANET2007_PRUEBAS.";
    }
}
