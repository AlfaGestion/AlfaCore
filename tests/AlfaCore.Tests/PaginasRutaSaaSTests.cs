using System.Reflection;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Las páginas de Components/Pages heredan SaaSRoutePageBase, que ya declara idweb/idbase:
/// volver a declararlos rompe la página al abrirla («more than one parameter matching the name»).</summary>
public sealed class PaginasRutaSaaSTests
{
    [Fact]
    public void NingunaPaginaRedeclaraLosParametrosDeRuta()
    {
        var baseType = typeof(AlfaCore.Components.Pages.SaaSRoutePageBase);
        var conflictos = baseType.Assembly.GetTypes()
            .Where(t => t != baseType && baseType.IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null
                    && (p.Name.Equals("idweb", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("idbase", StringComparison.OrdinalIgnoreCase)))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.Empty(conflictos);
    }
}
