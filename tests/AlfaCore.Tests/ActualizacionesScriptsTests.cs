using System.Text.RegularExpressions;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Scripts de App_Data/updates (2026-10-06): dos scripts con la misma versión (2026-09-23-001) hacían que el
/// panel y la aplicación automática fallaran en todas las bases, sin aplicar ningún script posterior.
/// Además, las bases de clientes están en compatibilidad SQL 2008 (nivel 100): TRY_CONVERT y otras
/// funciones de SQL 2012+ no existen ahí y el script falla.
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

    private static readonly Regex FuncionesSql2012 = new(
        @"\b(TRY_CONVERT|TRY_CAST|TRY_PARSE|IIF|CHOOSE|FORMAT|CONCAT|EOMONTH|DATEFROMPARTS|DATETIMEFROMPARTS|STRING_AGG|STRING_SPLIT)\s*\(",
        RegexOptions.IgnoreCase);

    [Fact]
    public void ScriptsNuevosNoUsanFuncionesQueNoExistenEnCompatibilidad2008()
    {
        // Rige desde 2026-10-06; los scripts anteriores que las mencionan lo hacen solo dentro de
        // reemplazos que se ajustan según el nivel de compatibilidad de la base.
        var conProblemas = Directory
            .GetFiles(UpdatesPath(), "*.sql", SearchOption.TopDirectoryOnly)
            .Select(ruta => (Ruta: ruta, Match: Formato.Match(Path.GetFileName(ruta))))
            .Where(x => x.Match.Success && string.CompareOrdinal(x.Match.Groups["version"].Value, "2026-10-06-001") >= 0)
            .Select(x => (Archivo: Path.GetFileName(x.Ruta), Funciones: FuncionesSql2012.Matches(SinComentarios(File.ReadAllText(x.Ruta)))
                .Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList()))
            .Where(x => x.Funciones.Count > 0)
            .Select(x => $"{x.Archivo}: {string.Join(", ", x.Funciones)}")
            .ToList();

        Assert.True(conProblemas.Count == 0,
            "Estos scripts usan funciones que no existen en bases con compatibilidad SQL 2008: " + string.Join(" | ", conProblemas));
    }

    private static string SinComentarios(string sql)
        => Regex.Replace(Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"--[^\r\n]*", " ");

    private static string UpdatesPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, "src", "AlfaCore", "App_Data", "updates");
    }
}
