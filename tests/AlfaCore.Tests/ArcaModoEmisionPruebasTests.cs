using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Solo lectura: modo de emisión de ALFANET2007_PRUEBAS (siempre homologación) y, si se pasa
/// ALFACORE_MODO_BASE_REAL (nombre de la base real del mismo servidor), el de esa base.
/// Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class ArcaModoEmisionPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task BaseDePruebaSiempreHomologacionYBaseRealSegunSuConfiguracion()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using (var cn = new SqlConnection(cs))
        {
            await cn.OpenAsync();
            Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
            var (modo, motivo) = await ArcaModoEmision.LeerAsync(cn, null, CancellationToken.None);
            output.WriteLine($"{cn.Database}: {modo} · {motivo}");
            Assert.Equal(ArcaModoEmision.Homologacion, modo);
            var emisor = await runtime.Config.ResolveEmisorAsync(cn, null, CancellationToken.None);
            if (emisor is not null)
            {
                Assert.True(emisor.EsBasePrueba);
                Assert.Equal(ArcaAmbiente.Homologacion, emisor.Ambiente);
                Assert.Contains("homo", emisor.WsfeUrl, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (Environment.GetEnvironmentVariable("ALFACORE_MODO_BASE_REAL") is not { Length: > 0 } real) return;
        await using var cnReal = new SqlConnection(new SqlConnectionStringBuilder(cs) { InitialCatalog = real }.ConnectionString);
        await cnReal.OpenAsync();
        var (modoReal, motivoReal) = await ArcaModoEmision.LeerAsync(cnReal, null, CancellationToken.None);
        output.WriteLine($"{real}: {modoReal} · {motivoReal}");
        Assert.NotEqual(ArcaModoEmision.Homologacion, modoReal);
        foreach (var unidad in new[] { "1", "2", "3" })
        {
            var e = await runtime.Config.ResolveEmisorAsync(cnReal, unidad, CancellationToken.None);
            output.WriteLine($"  U{unidad}: {(e is null ? "sin emisor" : $"{e.Cuit} PV {e.PuntoVentaElectronico} {e.Ambiente} prueba={e.EsBasePrueba} real={e.EmisionRealAlfaCore} {e.WsfeUrl}")}");
            if (e is null) continue;
            Assert.False(e.EsBasePrueba);
            Assert.Equal(ArcaAmbiente.Produccion, e.Ambiente);
            Assert.Equal(modoReal == ArcaModoEmision.Produccion, e.EmisionRealAlfaCore);
        }
    }
}
