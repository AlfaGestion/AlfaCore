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

    // Números de versión que ya se usaron con dos archivos distintos antes de esta prueba (2026-10-27). En
    // cada base sólo corrió el primero que llegó: el otro quedó salteado (ver panel Actualizaciones →
    // "salteados"). No agregar versiones acá: usar el siguiente número libre.
    private static readonly HashSet<string> ChoquesHistoricos = new(StringComparer.OrdinalIgnoreCase)
    {
        "2026-06-08-001", "2026-06-16-001", "2026-06-16-002", "2026-06-16-003", "2026-07-01-001", "2026-07-22-001",
        "2026-07-23-001", "2026-07-23-003",
        "2026-07-24-001", "2026-07-28-001", "2026-08-06-001", "2026-08-10-001", "2026-08-11-001", "2026-09-11-001",
        "2026-09-17-001", "2026-09-23-001", "2026-10-15-001", "2026-10-15-002", "2026-10-15-003", "2026-10-19-001",
        "2026-10-22-002"
    };

    [Fact]
    public void NingunNumeroDeVersionSeReusaConOtroArchivo()
    {
        // La carpeta puede no tener duplicados y aun así haber choque: un script se aplicó en las bases con
        // la versión X, se renombró, y otro archivo tomó X. Las bases que ya tenían X nunca corren el nuevo.
        var root = Directory.GetParent(UpdatesPath())!.Parent!.Parent!.Parent!.FullName;
        string salida;
        try
        {
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git",
                "log --all --name-only --diff-filter=A --format= -- src/AlfaCore/App_Data/updates")
            {
                WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            })!;
            salida = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0)
                return; // Sin repositorio git (ej. copia publicada): no hay historial que mirar.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return; // git no instalado.
        }

        var actuales = Directory.GetFiles(UpdatesPath(), "*.sql").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choques = salida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFileName)
            .Concat(actuales)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(nombre => Formato.Match(nombre!))
            .Where(m => m.Success)
            .GroupBy(m => m.Groups["version"].Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !ChoquesHistoricos.Contains(g.Key))
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(m => m.Value))}")
            .ToList();

        Assert.True(choques.Count == 0,
            "Estos números de versión ya los usó otro archivo (aunque se haya renombrado o borrado); las bases que " +
            "aplicaron el anterior nunca van a correr el nuevo. Usar el siguiente número libre: " + string.Join(" | ", choques));
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

    private static AlfaCore.Models.ActualizacionScriptDto Script(string archivo)
        => new() { Archivo = archivo, VersionKey = archivo.Split("__")[0] };

    [Fact]
    public void Salteados_DetectaVersionReusadaYFechaAtrasada_SinMarcarRenombresNiScriptsViejos()
    {
        var scripts = new[]
        {
            Script("2026-05-01-001__anterior_al_historial.sql"),
            Script("2026-10-19-001__caja_conciliacion_bancaria.sql"),          // versión ocupada por encuestas
            Script("2026-10-20-004__conversaciones_encuestas_satisfaccion.sql"), // renombre de la de encuestas
            Script("2026-10-21-001__subido_con_fecha_atrasada.sql"),
            Script("2026-10-22-001__aplicado_bien.sql"),
            Script("2026-10-30-001__pendiente_normal.sql"),
        };
        var aplicados = new[] { "2026-10-19-001__conversaciones_encuestas_satisfaccion.sql", "2026-10-22-001__aplicado_bien.sql" };

        var salteados = AlfaCore.Services.ActualizacionesService.CalcularSalteados(scripts, "2026-10-22-001", aplicados, "2026-06-01-001")
            .Select(s => s.Archivo).ToList();

        Assert.Equal(["2026-10-19-001__caja_conciliacion_bancaria.sql", "2026-10-21-001__subido_con_fecha_atrasada.sql"], salteados);
        Assert.Empty(AlfaCore.Services.ActualizacionesService.CalcularSalteados(scripts, null, aplicados, "2026-06-01-001"));
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
