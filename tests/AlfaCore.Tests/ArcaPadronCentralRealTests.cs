using AlfaCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Consulta real (producción ARCA, solo lectura) de la consulta central de padrón. Explícita: requiere
/// ALFACORE_PADRON_CENTRAL_SQL (base central con certificado PADRON) y ALFACORE_PADRON_CENTRAL=1.
/// Opcionales: ALFACORE_PADRON_CUIT y ALFACORE_PADRON_DNI.
/// </summary>
public sealed class ArcaPadronCentralRealTests(ITestOutputHelper output)
{
    [PadronCentralRealFact]
    public async Task ConsultaCuitYDniConCacheYRegistro()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AlfaGestion"] = Environment.GetEnvironmentVariable("ALFACORE_PADRON_CENTRAL_SQL"),
            ["ArcaPadron:DiasCache"] = "7"
        }).Build();
        var http = new ServiceCollection().AddHttpClient()
            .BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var central = new ArcaPadronCentralService(new ArcaConfigService(runtime.Events, runtime.Sessions, configuration),
            runtime.Wsaa, http, configuration, runtime.Events);

        var (cuitCentral, vence) = await central.EstadoCertificadoAsync();
        output.WriteLine($"Certificado central: CUIT {cuitCentral}, vence {vence:dd/MM/yyyy}");

        var cuit = Environment.GetEnvironmentVariable("ALFACORE_PADRON_CUIT") ?? "30688774930";
        var primera = await central.ConsultarCuitAsync(cuit, "PRUEBA-ALFACORE", "test");
        output.WriteLine($"CUIT {primera.Cuit}: {primera.RazonSocial} · {primera.CondicionIva} · {primera.Calle}, {primera.Localidad} ({primera.Provincia}) {primera.CodigoPostal}");
        var segunda = await central.ConsultarCuitAsync(cuit, "PRUEBA-ALFACORE", "test"); // sale de la cache
        Assert.Equal(primera.RazonSocial, segunda.RazonSocial);

        var dni = Environment.GetEnvironmentVariable("ALFACORE_PADRON_DNI") ?? "22807325";
        try
        {
            var personas = await central.ConsultarDniAsync(dni, "PRUEBA-ALFACORE", "test");
            output.WriteLine($"DNI {dni}: {personas.Count} CUIT");
            foreach (var p in personas) output.WriteLine($"  {p.Cuit}: {p.RazonSocial} · {p.CondicionIva}");
        }
        catch (InvalidOperationException ex)
        {
            output.WriteLine($"DNI {dni}: {ex.Message}");
        }
    }
}

public sealed class PadronCentralRealFactAttribute : FactAttribute
{
    public PadronCentralRealFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_PADRON_CENTRAL") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_PADRON_CENTRAL_SQL")))
            Skip = "Consulta real explícita: requiere ALFACORE_PADRON_CENTRAL=1 y ALFACORE_PADRON_CENTRAL_SQL.";
    }
}
