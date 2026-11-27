using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AlfaCore.Mcp.Infra;
using Microsoft.Data.SqlClient;
using ModelContextProtocol;

namespace AlfaCore.Mcp.Tools;

/// <summary>Respuesta comun de todas las herramientas: filas + totales + advertencias para que el modelo no invente contexto.</summary>
public sealed class ToolResponse
{
    public string Herramienta { get; set; } = "";
    /// <summary>Como se calcularon los importes (neto sin IVA, signo de NC, anulados, proformas).</summary>
    public string? Criterio { get; set; }
    public Dictionary<string, object?>? Totales { get; set; }
    public List<Dictionary<string, object?>> Filas { get; set; } = new();
    public int CantidadFilas => Filas.Count;
    public bool Truncado { get; set; }
    public List<string> Advertencias { get; } = new();
}

/// <summary>
/// Envuelve cada herramienta: verifica IA_MCP_HABILITADO, mide, traduce errores a mensajes seguros y audita en IA_LOG_CONSULTAS.
/// </summary>
public sealed class ToolRunner(IMcpConfigProvider config, IAuditLog audit, IMcpUserContext usuario, ILogger<ToolRunner> log)
{
    private static readonly JsonSerializerOptions JsonLog = new() { WriteIndented = false };

    public async Task<ToolResponse> RunAsync(string herramienta, object? parametros,
        Func<McpRuntimeSettings, Task<ToolResponse>> cuerpo, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        McpRuntimeSettings? cfg = null;
        string? error = null;
        int? filas = null;
        try
        {
            cfg = await config.GetAsync(ct);
            if (!cfg.Habilitado)
                throw new ToolUserException("El servidor MCP está deshabilitado (clave IA_MCP_HABILITADO).");

            var respuesta = await cuerpo(cfg);
            respuesta.Herramienta = herramienta;
            filas = respuesta.Filas.Count;
            return respuesta;
        }
        catch (ToolUserException ex)
        {
            error = ex.Message;
            throw new McpException(ex.Message);
        }
        catch (OperationCanceledException)
        {
            error = "Cancelada por el cliente.";
            throw;
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            error = "Timeout: " + ex.Message;
            throw new McpException($"La consulta superó el tiempo máximo de {cfg?.TimeoutSeg} s. Probá con un período más corto.");
        }
        catch (Exception ex)
        {
            var incidente = Guid.NewGuid().ToString("N")[..8];
            error = $"[{incidente}] {ex}";
            log.LogError(ex, "Falla en herramienta {Herramienta} (incidente {Incidente}).", herramienta, incidente);
            throw new McpException($"Error interno al ejecutar {herramienta} (incidente {incidente}). El detalle quedó en IA_LOG_CONSULTAS.");
        }
        finally
        {
            sw.Stop();
            var guardarParametros = cfg is { LogParametros: true } && parametros is not null;
            await audit.WriteAsync(new AuditEntry(
                usuario.UserName,
                herramienta,
                guardarParametros ? JsonSerializer.Serialize(parametros, JsonLog) : null,
                (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue),
                filas,
                error is { Length: > 4000 } ? error[..4000] : error), CancellationToken.None);
        }
    }
}

/// <summary>Validacion y normalizacion de los parametros que llegan del modelo (todo como texto, se valida aca).</summary>
public static class Parametros
{
    private static readonly string[] FormatosFecha = ["yyyy-MM-dd", "yyyyMMdd"];

    public static DateTime Fecha(string? valor, string nombre)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ToolUserException($"Falta el parámetro '{nombre}' (formato AAAA-MM-DD).");
        if (DateTime.TryParseExact(valor.Trim(), FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.Date;
        throw new ToolUserException($"'{nombre}' no es una fecha válida: '{valor}'. Usar AAAA-MM-DD (ej. 2026-10-31).");
    }

    /// <summary>Devuelve [desde, hasta+1día): el filtro SQL usa Fecha &gt;= desde AND Fecha &lt; hastaExclusivo.</summary>
    public static (DateTime Desde, DateTime HastaExclusivo) Rango(string? desde, string? hasta, string nombreDesde = "desde", string nombreHasta = "hasta")
    {
        var d = Fecha(desde, nombreDesde);
        var h = Fecha(hasta, nombreHasta);
        if (h < d) throw new ToolUserException($"'{nombreHasta}' ({h:yyyy-MM-dd}) es anterior a '{nombreDesde}' ({d:yyyy-MM-dd}).");
        return (d, h.AddDays(1));
    }

    public static int Top(int? top, McpRuntimeSettings cfg, ToolResponse respuesta)
    {
        var t = top ?? 10;
        if (t < 1) throw new ToolUserException("'top' debe ser 1 o más.");
        if (t <= cfg.TopMaximo) return t;
        respuesta.Advertencias.Add($"Se pidió top={t}; el máximo permitido es {cfg.TopMaximo} (IA_MCP_TOP_MAXIMO). Se devolvieron {cfg.TopMaximo}.");
        return cfg.TopMaximo;
    }

    public const int MaxPalabras = 5;

    /// <summary>
    /// Separa un texto de busqueda en palabras (todas deben aparecer, en cualquier orden). Max 5 palabras, cada una de 1 a 50 caracteres,
    /// y al menos una de 2 o mas para no devolver toda la tabla.
    /// </summary>
    public static string[] Palabras(string? texto, string nombre)
    {
        if (string.IsNullOrWhiteSpace(texto)) throw new ToolUserException($"Falta el parámetro '{nombre}' (texto a buscar).");
        var p = texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (p.Length > MaxPalabras) throw new ToolUserException($"'{nombre}' tiene demasiadas palabras (máximo {MaxPalabras}). Usar las más distintivas.");
        if (p.Any(w => w.Length > 50)) throw new ToolUserException($"'{nombre}' tiene una palabra de más de 50 caracteres.");
        if (p.All(w => w.Length < 2)) throw new ToolUserException($"'{nombre}' debe tener al menos una palabra de 2 o más caracteres.");
        return p;
    }

    /// <summary>Patron LIKE '%palabra%' con los comodines de T-SQL escapados (la palabra se busca literal).</summary>
    public static string Contiene(string palabra) =>
        "%" + palabra.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

    /// <summary>Codigo exacto de cliente / articulo tal como lo devuelven las busquedas (sin relleno).</summary>
    public static string Codigo(string? valor, string nombre) =>
        CodigoOpcional(valor, nombre) ?? throw new ToolUserException($"Falta el parámetro '{nombre}' (código exacto; si no lo sabés, buscarlo primero).");

    public static string? CodigoOpcional(string? valor, string nombre)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var v = valor.Trim();
        if (v.Length > 50) throw new ToolUserException($"'{nombre}' no parece un código (más de 50 caracteres). Buscarlo primero con la herramienta de búsqueda.");
        return v;
    }

    /// <summary>Normaliza una opcion de lista cerrada (sin tildes, minusculas). Devuelve la forma canonica de <paramref name="validas"/>.</summary>
    public static string Opcion(string? valor, string nombre, string[] validas, string defecto)
    {
        if (string.IsNullOrWhiteSpace(valor)) return defecto;
        var buscado = valor.Trim().ToLowerInvariant();
        var hit = validas.FirstOrDefault(v => v.Equals(buscado, StringComparison.OrdinalIgnoreCase));
        return hit ?? throw new ToolUserException($"'{nombre}' inválido: '{valor}'. Valores permitidos: {string.Join(", ", validas)}.");
    }
}
