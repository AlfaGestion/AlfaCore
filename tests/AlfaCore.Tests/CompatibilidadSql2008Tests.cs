using System.Text.RegularExpressions;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Hay bases de clientes en SQL Server 2008 con compatibilidad 80 (GENESIS, 2026-10-28). Ahí no existen
/// OFFSET/FETCH, TRY_CAST, CONCAT, THROW, LAG/LEAD, JSON, etc.: la consulta falla con error de sintaxis y la
/// pantalla entera queda inutilizable. Esta prueba frena que vuelvan a entrar en el SQL embebido en C#.
/// </summary>
public sealed class CompatibilidadSql2008Tests
{
    // Mayúsculas a propósito (SQL del proyecto): así no coincide con string.Concat ni con .Trim() de C#.
    private static readonly Regex SintaxisNueva = new(
        @"OFFSET\s+[@\w]+\s+ROWS|FETCH\s+NEXT\s+(?!FROM\b)|\b(TRY_CONVERT|TRY_CAST|TRY_PARSE|IIF|CHOOSE|CONCAT|CONCAT_WS|EOMONTH|DATEFROMPARTS|" +
        @"STRING_AGG|STRING_SPLIT|ISJSON|JSON_VALUE|JSON_QUERY|OPENJSON|LAG|LEAD|FIRST_VALUE|LAST_VALUE)\s*\(|FOR JSON|" +
        @"AT TIME ZONE|\bTHROW\s+\d|CREATE OR ALTER");

    // Pendientes conocidos. No agregar archivos: escribir el SQL compatible (ROW_NUMBER, ISNULL(...)+..., RAISERROR).
    private static readonly HashSet<string> Pendientes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConversacionesService.cs",                 // JSON y zona horaria; Conversaciones no corre en SQL 2008
        "ConciliacionBancariaService.cs",           // huellas HASHBYTES(CONCAT(...)) ya grabadas en otras bases
        "ConciliacionBancariaService.Ia.cs",
        "ConciliacionBancariaService.Vinculos.cs",
        "ConciliacionTarjetasService.Cuenta.cs",
        "ConciliacionTarjetasService.Movimientos.cs",
        "CargaComprobantesPlanificador.cs",         // FOR JSON para la foto previa a grabar
        "CargaComprobantesAsociadoPreparador.cs",
        "BillingService.cs",                        // corren contra ALFA_CENTRAL (SQL 2016)
        "PlanesService.cs",
        "CentralIaSchema.cs",
    };

    [Fact]
    public void SqlEmbebidoNoUsaSintaxisQueNoExisteEnSql2008()
    {
        var src = Path.Combine(RepoRoot(), "src", "AlfaCore");
        var problemas = new[] { "Services", "Repositories" }
            .SelectMany(carpeta => new[] { "*.cs", "*.sql" }
                .SelectMany(patron => Directory.GetFiles(Path.Combine(src, carpeta), patron, SearchOption.AllDirectories)))
            .Where(ruta => !Pendientes.Contains(Path.GetFileName(ruta)))
            .SelectMany(ruta => File.ReadAllLines(ruta)
                .Select((linea, i) => (Linea: linea, Numero: i + 1))
                .Where(x => !x.Linea.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Select(x => (x.Numero, Match: SintaxisNueva.Match(x.Linea)))
                .Where(x => x.Match.Success)
                .Select(x => $"{Path.GetFileName(ruta)}:{x.Numero} ({x.Match.Value.Trim()})"))
            .ToList();

        Assert.True(problemas.Count == 0,
            "SQL que falla en bases SQL Server 2008 / compatibilidad 80: " + string.Join(" | ", problemas));
    }

    private static readonly Regex ThrowSql = new(@"\bTHROW\b");

    [Fact]
    public void ScriptsNuevosNoUsanThrow()
    {
        // THROW no existe en SQL 2008: el lote entero no compila aunque la rama no se ejecute, y el actualizador
        // se frena ahí para siempre en esa base. Rige desde 2026-10-28 (los anteriores ya se corrigieron).
        var formato = new Regex(@"^(?<version>\d{4}-\d{2}-\d{2}-\d{3})__.+\.sql$", RegexOptions.IgnoreCase);
        var conThrow = Directory
            .GetFiles(Path.Combine(RepoRoot(), "src", "AlfaCore", "App_Data", "updates"), "*.sql")
            .Where(ruta => formato.Match(Path.GetFileName(ruta)) is { Success: true } m
                && string.CompareOrdinal(m.Groups["version"].Value, "2026-05-27-001") >= 0)
            .Where(ruta => ThrowSql.IsMatch(SinComentarios(File.ReadAllText(ruta))))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(conThrow.Count == 0, "Usar RAISERROR + RETURN en vez de THROW: " + string.Join(" | ", conThrow));
    }

    private static string SinComentarios(string sql)
        => Regex.Replace(Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"--[^\r\n]*", " ");

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;

        Assert.NotNull(current);
        return current!.FullName;
    }
}
