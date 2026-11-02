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

/// <summary>
/// Emisión real en homologación contra ALFANET2007_PRUEBAS de los clientes de ALFACORE_FA_EMITIR (separados
/// por coma): pide CAE de prueba, graba la factura y actualiza la facturación automática. Repetirla el mismo
/// día devuelve la misma factura (borrador determinístico).
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesFacturacionAutomaticaEmisionPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaEmisionFact]
    public async Task EmiteEnHomologacionLosClientesIndicados()
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
        foreach (var cuenta in Environment.GetEnvironmentVariable("ALFACORE_FA_EMITIR")!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            CargaComprobantesGrabacionResultado resultado;
            try
            {
                var (borrador, talonario, fila) = await CargaComprobantesFacturacionAutomatica.PrepararAsync(cn, ctx, cuenta, hoy, default);
                var plan = await emision.ValidarAsync(cn, ctx, 0, talonario, [], default, borrador);
                resultado = await emision.EmitirAsync(cn, ctx, 0, talonario, [], Guid.NewGuid(), plan.Huella, () => { }, default, borrador);
                output.WriteLine($"{cuenta} {fila.Cliente}: {resultado.Estado} {resultado.Tc} {resultado.Comprobante} · CAE {resultado.Cae} vto {resultado.Vencimiento:dd/MM/yyyy} · asiento {resultado.NumeroAsiento} · total {plan.Liquidacion.Total:N2} · {resultado.Mensaje}");
            }
            catch (Exception ex) when (ex is CargaComprobantesReglaException or SqlException)
            {
                output.WriteLine($"{cuenta}: ERROR {ex.Message}");
                continue;
            }
            if (resultado.FacturaId is not { } id) continue;
            var cab = await cn.QuerySingleAsync("SELECT TC,IDCOMPROBANTE,FECHA,IMPORTE,IMPORTE_S_IVA,ImporteInsumos,ImporteOtrosConceptos,ImporteIva,NetoGravado,CodCpteAfip,CONVERT(nvarchar(200),OBSERVACIONES) Obs FROM V_MV_CPTE WHERE ID=@id", new { id });
            output.WriteLine($"   cabecera: {cab.TC} {cab.IDCOMPROBANTE} total {cab.IMPORTE:N2} neto {cab.IMPORTE_S_IVA:N2} insumos {cab.ImporteInsumos:N2} otros {cab.ImporteOtrosConceptos:N2} iva {cab.ImporteIva:N2} afip {cab.CodCpteAfip} · {cab.Obs}");
            foreach (var o in await cn.QueryAsync("SELECT SECUENCIA,OBSERVACION,IMPORTE,IMPORTE_S_IVA FROM V_MV_CPTE_OBSERV WHERE TC=@TC AND IDCOMPROBANTE=@IDCOMPROBANTE ORDER BY SECUENCIA", new { cab.TC, cab.IDCOMPROBANTE }))
                output.WriteLine($"   renglón {o.SECUENCIA}: {o.OBSERVACION} · {o.IMPORTE_S_IVA:N2} (c/IVA {o.IMPORTE:N2})");
            string comprobante = cab.IDCOMPROBANTE;
            foreach (var a in await cn.QueryAsync("SELECT SECUENCIA,CUENTA,[DEBE-HABER] DH,IMPORTE,PERIODO,LIVA_ImpNetoGrav,LIVA_ImpIVA,LIVA_TOTAL FROM MV_ASIENTOS WHERE SUCURSAL=LEFT(@c,4) AND NUMERO=SUBSTRING(@c,5,8) AND LETRA=RIGHT(@c,1) AND FECHA=@f ORDER BY SECUENCIA", new { c = comprobante, f = hoy }))
                output.WriteLine($"   asiento {a.SECUENCIA}: {a.CUENTA} {a.DH} {a.IMPORTE:N2} · periodo {a.PERIODO} · libro neto {a.LIVA_ImpNetoGrav} iva {a.LIVA_ImpIVA} total {a.LIVA_TOTAL}");
            var fa = await cn.QuerySingleAsync("SELECT TOP 1 FechaUltMov FROM MA_CUENTAS_AUTOCPTES WHERE Cuenta=@cuenta ORDER BY SucursalCuenta", new { cuenta });
            output.WriteLine($"   facturación automática: última factura {fa.FechaUltMov:dd/MM/yyyy}");
        }
    }
}

public sealed class FacturacionAutomaticaEmisionFactAttribute : FactAttribute
{
    public FacturacionAutomaticaEmisionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_FA_EMITIR")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")))
            Skip = "Emisión explícita en homologación: requiere ALFACORE_FA_EMITIR=cuenta[,cuenta] y ALFACORE_TEST_SQL hacia ALFANET2007_PRUEBAS.";
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
