using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>AlfaActualizacion_Monitor (2026-10-29): qué versión le toca a cada cliente.</summary>
public sealed class MonitorActualizacionTests
{
    private static MonitorActualizacionDto V(string version, string liberacion, string? prueba = null)
        => new() { IdVersion = 1, Version = version, ArchivoRemoto = $"ACTUALIZACIONES/{version}/x.exe", Sha256 = new string('a', 64), TamanioBytes = 10, Liberacion = liberacion, ClientesPrueba = prueba };

    [Theory]
    [InlineData("20261029-01", true)]
    [InlineData("20261029-1", false)]
    [InlineData("2026-10-29", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsVersionValida(string? version, bool esperado)
        => Assert.Equal(esperado, MonitorClientesService.EsVersionValida(version));

    [Fact]
    public void Pausada_NoLeLlegaANadie_NiAPrueba()
        => Assert.Null(MonitorClientesService.ElegirActualizacion([V("20261029-01", MonitorLiberacion.Nadie, "C1")], "C1", "1", null));

    [Fact]
    public void Prueba_SoloLeLlegaALosClientesDePrueba()
    {
        var versiones = new[] { V("20261029-01", MonitorLiberacion.Prueba, "C1, C2") };
        Assert.NotNull(MonitorClientesService.ElegirActualizacion(versiones, "c2", "4", null));
        Assert.Null(MonitorClientesService.ElegirActualizacion(versiones, "C3", "1", null));
    }

    [Fact]
    public void P1P2_LeLlegaAPrioritariosYAPrueba()
    {
        var versiones = new[] { V("20261029-01", MonitorLiberacion.P1P2, "C9") };
        Assert.NotNull(MonitorClientesService.ElegirActualizacion(versiones, "C1", "2", null));
        Assert.Null(MonitorClientesService.ElegirActualizacion(versiones, "C1", "3", null));
        Assert.NotNull(MonitorClientesService.ElegirActualizacion(versiones, "C9", "4", null));
    }

    [Fact]
    public void Todos_LeLlegaATodos()
        => Assert.NotNull(MonitorClientesService.ElegirActualizacion([V("20261029-01", MonitorLiberacion.Todos)], "C1", null, null));

    [Fact]
    public void EligeLaMasNueva_YSoloSiEsPosteriorALaInstalada()
    {
        var versiones = new[]
        {
            V("20261029-01", MonitorLiberacion.Todos),
            V("20261105-02", MonitorLiberacion.Todos),
            V("20261105-03", MonitorLiberacion.Nadie)
        };
        Assert.Equal("20261105-02", MonitorClientesService.ElegirActualizacion(versiones, "C1", "1", null)!.Version);
        Assert.Equal("20261105-02", MonitorClientesService.ElegirActualizacion(versiones, "C1", "1", "20261029-01")!.Version);
        Assert.Null(MonitorClientesService.ElegirActualizacion(versiones, "C1", "1", "20261105-02"));
    }

    [Fact]
    public void VersionInstaladaInvalida_SeTomaComoSinVersion()
        => Assert.NotNull(MonitorClientesService.ElegirActualizacion([V("20261029-01", MonitorLiberacion.Todos)], "C1", "1", "1.0.436"));
}
