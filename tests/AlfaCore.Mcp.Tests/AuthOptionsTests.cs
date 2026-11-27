using AlfaCore.Mcp.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AlfaCore.Mcp.Tests;

public class AuthOptionsTests
{
    private sealed class Entorno(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "AlfaCore.Mcp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static AuthOptions Cargar(string entorno, string? signingKeyPath)
    {
        var valores = new Dictionary<string, string?> { ["Mcp:Auth:Issuer"] = "https://alfacentral.ddns.net:8443" };
        if (signingKeyPath is not null) valores["Mcp:Auth:SigningKeyPath"] = signingKeyPath;
        return AuthOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(valores).Build(), new Entorno(entorno));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("App_Data/mcp-signing-key.pem")]
    public void En_produccion_la_clave_de_firma_exige_ruta_absoluta(string? ruta)
    {
        // Con ruta relativa, crear-usuario y el sitio podrian usar archivos *.totp-key distintos y nadie podria entrar.
        var ex = Assert.Throws<InvalidOperationException>(() => Cargar("Production", ruta));
        Assert.Contains("SigningKeyPath", ex.Message);
    }

    [Fact]
    public void En_produccion_acepta_una_ruta_absoluta()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "AlfaCoreMcp", "signing-key.pem");
        Assert.Equal(ruta, Cargar("Production", ruta).SigningKeyPath);
    }

    [Fact]
    public void En_desarrollo_sigue_valiendo_la_ruta_por_defecto() =>
        Assert.Equal("App_Data/mcp-signing-key.pem", Cargar("Development", null).SigningKeyPath);
}
