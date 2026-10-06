using System.Text.RegularExpressions;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Scripts de App_Data/updates (2026-10-06): dos scripts con la misma versión (2026-09-23-001) hacían que el
/// panel y la aplicación automática fallaran en todas las bases, sin aplicar ningún script posterior.
/// </summary>
public sealed class ActualizacionesScriptsTests
{
    private static readonly Regex Formato = new(@"^(?<version>\d{4}-\d{2}-\d{2}-\d{3})__.+\.sql$", RegexOptions.IgnoreCase);

    [Fact]
    public void CadaVersionTieneUnSoloScript()
    {
        var duplicadas = Directory
            .GetFiles(UpdatesPath(), "*.sql", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Select(nombre => Formato.Match(nombre!))
            .Where(m => m.Success)
            .GroupBy(m => m.Groups["version"].Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(m => m.Value))}")
            .ToList();

        Assert.True(duplicadas.Count == 0,
            "Hay scripts con la misma versión (usar el siguiente número libre): " + string.Join(" | ", duplicadas));
    }

    private static string UpdatesPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, "src", "AlfaCore", "App_Data", "updates");
    }
}
