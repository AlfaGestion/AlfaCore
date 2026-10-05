using System.Text.RegularExpressions;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Engranaje y página /configuracion (2026-10-05): lista única de configuraciones.</summary>
public sealed class ConfiguracionCatalogoTests
{
    [Fact]
    public void CadaRutaDelCatalogo_ExisteComoPagina()
    {
        var pages = Directory.GetFiles(Path.Combine(FindRepositoryRoot(), "src", "AlfaCore", "Components"), "*.razor", SearchOption.AllDirectories);
        var rutas = pages
            .SelectMany(p => Regex.Matches(File.ReadAllText(p), "@page \"(?<r>/[^\"]*)\"").Select(m => m.Groups["r"].Value))
            .Select(r => r.TrimEnd('/').ToLowerInvariant())
            .ToHashSet();

        foreach (var entrada in ConfiguracionCatalogo.Entradas)
            Assert.True(rutas.Contains(entrada.RutaBase.ToLowerInvariant()), $"No existe una página para {entrada.Ruta} ({entrada.Clave}).");
    }

    [Fact]
    public void Claves_SonUnicasYCadaAreaEstaDeclarada()
    {
        Assert.Equal(ConfiguracionCatalogo.Entradas.Count, ConfiguracionCatalogo.Entradas.Select(e => e.Clave).Distinct().Count());
        Assert.All(ConfiguracionCatalogo.Entradas, e => Assert.Contains(e.Area, ConfiguracionCatalogo.Areas));
    }

    [Fact]
    public void Visibles_FiltraPorModulosYRutasHabilitadas()
    {
        var visibles = ConfiguracionCatalogo.Visibles(["/conversaciones", "/ventas"], ["/configuracion-general/logo"]);

        Assert.Contains(visibles, e => e.Clave == "conversaciones");
        Assert.Contains(visibles, e => e.Clave == "conv-horario");
        Assert.Contains(visibles, e => e.Clave == "puntos-venta");
        Assert.Contains(visibles, e => e.Clave == "logo");
        Assert.DoesNotContain(visibles, e => e.Clave == "usuarios");
        Assert.DoesNotContain(visibles, e => e.Clave == "empresa");
    }

    [Fact]
    public void Visibles_SinMenuMuestraTodas()
        => Assert.Equal(ConfiguracionCatalogo.Entradas.Count, ConfiguracionCatalogo.Visibles([], []).Count);

    [Fact]
    public void DelModulo_PonePrimeroLaPrincipal()
    {
        var delModulo = ConfiguracionCatalogo.DelModulo(ConfiguracionCatalogo.Entradas, "conversaciones");

        Assert.Equal("conversaciones", delModulo[0].Clave);
        Assert.All(delModulo, e => Assert.Contains("conversaciones", e.ModulosRelacionados));
        Assert.Contains(ConfiguracionCatalogo.DelModulo(ConfiguracionCatalogo.Entradas, "ventas"), e => e.Clave == "ventas");
        Assert.Empty(ConfiguracionCatalogo.DelModulo(ConfiguracionCatalogo.Entradas, ""));
    }

    [Theory]
    [InlineData("horario", "conv-horario")]
    [InlineData("HORARIO atención", "conv-horario")]
    [InlineData("razon social", "empresa")]
    [InlineData("permisos", "autorizacion-tareas")]
    [InlineData("smtp", "email")]
    [InlineData("capacitación", "reuniones-publicas")]
    public void Buscar_IgnoraAcentosYMayusculas(string texto, string claveEsperada)
        => Assert.Equal(claveEsperada, ConfiguracionCatalogo.Buscar(ConfiguracionCatalogo.Entradas, texto)[0].Clave);

    [Fact]
    public void Buscar_SinTextoDevuelveTodo()
        => Assert.Equal(ConfiguracionCatalogo.Entradas.Count, ConfiguracionCatalogo.Buscar(ConfiguracionCatalogo.Entradas, "  ").Count);

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AlfaCore")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
