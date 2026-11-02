using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Ensayo explícito contra ALFANET2007_PRUEBAS: arma la factura automática de cada cliente en condiciones
/// y ejecuta la validación del motor (grabación completa revertida). No pide CAE ni deja cambios.
/// Requiere ALFACORE_TEST_SQL apuntando a ALFANET2007_PRUEBAS y ALFACORE_FA_PRUEBAS=1.
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesFacturacionAutomaticaPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task ValidaLaFacturaAutomaticaDeCadaClienteSinGrabar()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var repo = new CargaComprobantesRepository();
        var ctx = await repo.GetContextoAsync(cn, Environment.GetEnvironmentVariable("ALFACORE_FA_USUARIO") ?? "Administrador", default);
        var planner = new CargaComprobantesPlanificador(repo);
        var sender = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var emision = new CargaComprobantesEmisionService(planner, new(planner, new()), sender, runtime.Config, runtime.Events);
        var hoy = await cn.ExecuteScalarAsync<DateTime>("SELECT CONVERT(date,GETDATE())");
        var filas = await CargaComprobantesFacturacionAutomatica.BuscarAsync(cn, ctx,
            new CargaComprobantesFiltro { TipoOrigen = CargaComprobantesFacturacionAutomatica.TipoOrigen, FechaFacturacion = hoy }, default);
        output.WriteLine($"{filas.Count} clientes en condiciones al {hoy:dd/MM/yyyy}");
        var errores = new Dictionary<string, int>();
        foreach (var fila in filas)
        {
            try
            {
                var (borrador, talonario, _) = await CargaComprobantesFacturacionAutomatica.PrepararAsync(cn, ctx, fila.Cuenta, hoy, default);
                var plan = await emision.ValidarAsync(cn, ctx, 0, talonario, [], default, borrador);
                output.WriteLine($"OK  {fila.Cuenta} U{fila.UnidadNegocio} tal {talonario} {plan.Letra} {plan.PuntoVenta:0000} total {plan.Liquidacion.Total:N2} " +
                    $"(listado {fila.Importe:N2}) · {plan.Origen.Lineas.Count} renglones · {plan.Origen.Observaciones}");
            }
            catch (Exception ex) when (ex is CargaComprobantesReglaException or SqlException)
            {
                var motivo = ex.Message.Split('\n')[0];
                errores[motivo] = errores.GetValueOrDefault(motivo) + 1;
                output.WriteLine($"ERR {fila.Cuenta} U{fila.UnidadNegocio}: {motivo}");
            }
        }
        foreach (var e in errores.OrderByDescending(x => x.Value)) output.WriteLine($"{e.Value} x {e.Key}");
        // Nada quedó grabado: la validación siempre revierte.
        Assert.Equal(filas.Count, (await CargaComprobantesFacturacionAutomatica.BuscarAsync(cn, ctx,
            new CargaComprobantesFiltro { TipoOrigen = CargaComprobantesFacturacionAutomatica.TipoOrigen, FechaFacturacion = hoy }, default)).Count);
    }
}

public sealed class FacturacionAutomaticaPruebasFactAttribute : FactAttribute
{
    public FacturacionAutomaticaPruebasFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_FA_PRUEBAS") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")))
            Skip = "Ensayo explícito: requiere ALFACORE_FA_PRUEBAS=1 y ALFACORE_TEST_SQL hacia ALFANET2007_PRUEBAS.";
    }
}
