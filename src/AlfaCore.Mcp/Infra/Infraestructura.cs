using System.Collections.Concurrent;
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Mcp.Infra;

/// <summary>Error pensado para mostrarse tal cual al usuario del MCP (mensaje corto, en español, sin detalles internos).</summary>
public sealed class ToolUserException(string message) : Exception(message);

// ---- Puntos de extension para la etapa 5 (OAuth + multi-base) -------------------------------------------------------

/// <summary>
/// Resuelve la cadena de conexion de la base a consultar. Hoy hay UNA base fija (variable de entorno / user-secrets).
/// Etapa 5: resolver por tenant segun las claims del token OAuth (ALFA_CENTRAL.bases), sin tocar las herramientas.
/// </summary>
public interface IConexionResolver
{
    Task<string> GetConnectionStringAsync(CancellationToken ct);
}

public sealed class ConexionUnicaResolver(string connectionString) : IConexionResolver
{
    public Task<string> GetConnectionStringAsync(CancellationToken ct) => Task.FromResult(connectionString);
}

/// <summary>
/// Identidad de quien consulta, para la auditoria. Hoy es un nombre fijo; etapa 5: claim del token OAuth.
/// </summary>
public interface IMcpUserContext
{
    string UserName { get; }
}

public sealed class UsuarioFijoContext(string userName) : IMcpUserContext
{
    public string UserName { get; } = userName;
}

// ---- Configuracion (TA_CONFIGURACION, claves IA_MCP_*, via dir.vw_Config) --------------------------------------------

/// <summary>Valores efectivos de las claves IA_MCP_*. Si una clave falta, se usan estos defaults (los de riesgo, apagados).</summary>
public sealed record McpRuntimeSettings(
    bool Habilitado,
    int MaxFilas,
    int TopMaximo,
    int TimeoutSeg,
    bool IncluirProformas,
    bool ConsultarSqlHabilitado,
    bool LogParametros,
    decimal AdvertirSinCostoPct)
{
    public static McpRuntimeSettings Desde(IReadOnlyDictionary<string, string> v) => new(
        Habilitado: Si(Get(v, "IA_MCP_HABILITADO"), false),
        MaxFilas: Entero(Get(v, "IA_MCP_MAX_FILAS"), 500, 1, 5000),
        TopMaximo: Entero(Get(v, "IA_MCP_TOP_MAXIMO"), 100, 1, 1000),
        TimeoutSeg: Entero(Get(v, "IA_MCP_TIMEOUT_SEG"), 60, 1, 600),
        IncluirProformas: Si(Get(v, "IA_MCP_INCLUIR_PROFORMAS"), true),
        ConsultarSqlHabilitado: Si(Get(v, "IA_MCP_CONSULTAR_SQL_HABILITADO"), false),
        LogParametros: Si(Get(v, "IA_MCP_LOG_PARAMETROS"), true),
        AdvertirSinCostoPct: Decimal(Get(v, "IA_MCP_ADVERTIR_SIN_COSTO_PCT"), 5m));

    private static string? Get(IReadOnlyDictionary<string, string> v, string clave) =>
        v.TryGetValue(clave, out var valor) ? valor : null;

    public static bool Si(string? valor, bool defecto) =>
        string.IsNullOrWhiteSpace(valor)
            ? defecto
            : valor.Trim().ToUpperInvariant() is "SI" or "SÍ" or "S" or "1" or "TRUE" or "YES";

    public static int Entero(string? valor, int defecto, int min, int max) =>
        int.TryParse(valor?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, min, max) : defecto;

    public static decimal Decimal(string? valor, decimal defecto) =>
        decimal.TryParse(valor?.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : defecto;
}

public interface IMcpConfigProvider
{
    Task<McpRuntimeSettings> GetAsync(CancellationToken ct);
}

/// <summary>Lee dir.vw_Config (unica puerta a TA_CONFIGURACION) con cache corto, separado por conexion.</summary>
public sealed class McpConfigProvider(IConexionResolver resolver, ILogger<McpConfigProvider> log) : IMcpConfigProvider
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, (DateTime Hasta, McpRuntimeSettings Valores)> _cache = new();

    public async Task<McpRuntimeSettings> GetAsync(CancellationToken ct)
    {
        var cs = await resolver.GetConnectionStringAsync(ct);
        if (_cache.TryGetValue(cs, out var hit) && hit.Hasta > DateTime.UtcNow) return hit.Valores;

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);
            var filas = await cn.QueryAsync<(string Clave, string Valor)>(
                new CommandDefinition("SELECT Clave, Valor FROM dir.vw_Config", cancellationToken: ct));
            var dic = filas.ToDictionary(f => f.Clave.Trim().ToUpperInvariant(), f => f.Valor, StringComparer.OrdinalIgnoreCase);
            var valores = McpRuntimeSettings.Desde(dic);
            _cache[cs] = (DateTime.UtcNow + Vigencia, valores);
            return valores;
        }
        catch (SqlException ex) when (ex.Number == 208) // objeto inexistente
        {
            throw new ToolUserException("Falta instalar la capa semántica en la base (esquema dir). Ejecutar los scripts de docs/base-datos/sql-referencia/dir-mcp.");
        }
        catch (SqlException ex) when (hit.Valores is not null)
        {
            // Si la base no responde un instante, se sigue con los ultimos valores conocidos.
            log.LogWarning(ex, "No se pudo refrescar IA_MCP_*; se usan los valores anteriores.");
            return hit.Valores;
        }
    }
}

// ---- Ejecucion SQL de solo lectura --------------------------------------------------------------------------------

public sealed record QueryResult(List<Dictionary<string, object?>> Filas, bool Truncado);

public class SqlExecutor(IConexionResolver resolver)
{
    /// <summary>
    /// Ejecuta una consulta de lectura y devuelve como maximo <paramref name="maxFilas"/> filas (si hay mas, corta y marca Truncado).
    /// Antepone SET DATEFORMAT ymd para neutralizar el literal 'aaaa-mm-dd' leido como a-d-m en bases con idioma español.
    /// </summary>
    public virtual async Task<QueryResult> QueryAsync(McpRuntimeSettings cfg, string sql, IReadOnlyDictionary<string, object?>? parametros,
        CancellationToken ct, int? maxFilas = null)
    {
        var tope = Math.Clamp(maxFilas ?? cfg.MaxFilas, 1, cfg.MaxFilas);
        await using var cn = new SqlConnection(await resolver.GetConnectionStringAsync(ct));
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SET DATEFORMAT ymd;\n" + sql;
        cmd.CommandTimeout = cfg.TimeoutSeg;
        if (parametros is not null)
            foreach (var (nombre, valor) in parametros)
                cmd.Parameters.AddWithValue("@" + nombre.TrimStart('@'), valor ?? DBNull.Value);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var nombres = new string[rd.FieldCount];
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nombres.Length; i++)
        {
            var n = string.IsNullOrWhiteSpace(rd.GetName(i)) ? $"col{i + 1}" : rd.GetName(i);
            while (!vistos.Add(n)) n += "_";
            nombres[i] = n;
        }

        var filas = new List<Dictionary<string, object?>>();
        var truncado = false;
        while (await rd.ReadAsync(ct))
        {
            if (filas.Count >= tope) { truncado = true; cmd.Cancel(); break; }
            var fila = new Dictionary<string, object?>(nombres.Length);
            for (var i = 0; i < nombres.Length; i++) fila[nombres[i]] = Convertir(rd.GetValue(i));
            filas.Add(fila);
        }
        return new QueryResult(filas, truncado);
    }

    private static object? Convertir(object valor) => valor switch
    {
        DBNull => null,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        byte[] => "(binario)",
        Guid g => g.ToString(),
        _ => valor
    };
}

// ---- Auditoria (dbo.IA_LOG_CONSULTAS, el login solo tiene INSERT) -----------------------------------------------------

public sealed record AuditEntry(string? Usuario, string Herramienta, string? Parametros, int DuracionMs, int? Filas, string? Error);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entrada, CancellationToken ct);
}

public sealed class SqlAuditLog(IConexionResolver resolver, ILogger<SqlAuditLog> log) : IAuditLog
{
    public async Task WriteAsync(AuditEntry e, CancellationToken ct)
    {
        try
        {
            await using var cn = new SqlConnection(await resolver.GetConnectionStringAsync(ct));
            await cn.OpenAsync(ct);
            await cn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.IA_LOG_CONSULTAS (Usuario, Herramienta, Parametros, DuracionMs, Filas, Error) " +
                "VALUES (@Usuario, @Herramienta, @Parametros, @DuracionMs, @Filas, @Error)",
                new { e.Usuario, e.Herramienta, e.Parametros, e.DuracionMs, e.Filas, e.Error }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // La auditoria no debe tumbar la consulta (disponibilidad > trazabilidad); queda en el log de la aplicacion.
            log.LogError(ex, "No se pudo grabar IA_LOG_CONSULTAS ({Herramienta}).", e.Herramienta);
        }
    }
}
