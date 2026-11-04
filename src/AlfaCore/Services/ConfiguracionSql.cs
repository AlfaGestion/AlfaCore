using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Lectura y grabación clave por clave de TA_CONFIGURACION (docs/CONFIGURACION_GLOBAL.md): se busca siempre por
/// CLAVE sin distinguir mayúsculas, la fila existente conserva su GRUPO y solo las claves nuevas reciben el grupo indicado.
/// </summary>
public static class ConfiguracionSql
{
    /// <summary>VALOR de cada clave, indexado por CLAVE en mayúsculas (como Cfg() del legacy).</summary>
    public static async Task<Dictionary<string, string>> LeerAsync(SqlConnection cn, SqlTransaction? tx, IEnumerable<string> claves, CancellationToken ct)
    {
        var lista = claves.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToArray();
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lista.Length == 0) return r;
        var filas = await cn.QueryAsync<(string Clave, string? Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), VALOR
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;
            """, new { Claves = lista }, tx, cancellationToken: ct));
        foreach (var f in filas)
            r[f.Clave] = (f.Valor ?? string.Empty).Trim();
        return r;
    }

    public static string Valor(IReadOnlyDictionary<string, string> cfg, string clave) => cfg.TryGetValue(clave.ToUpperInvariant(), out var v) ? v : string.Empty;

    public static bool EsSi(IReadOnlyDictionary<string, string> cfg, string clave) => Valor(cfg, clave).Equals("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>Actualiza la clave si existe (sin tocar su GRUPO) o la agrega con el grupo indicado.</summary>
    public static Task GrabarAsync(SqlConnection cn, SqlTransaction tx, string clave, string valor, string grupo, string descripcion, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            IF EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave))
                UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);
            ELSE
                INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
                VALUES (@Grupo, @Clave, @Valor, @Descripcion, GETDATE());
            """, new
        {
            Clave = clave.Trim(),
            Valor = valor.Trim(),
            Grupo = grupo,
            Descripcion = descripcion.Length > 50 ? descripcion[..50] : descripcion
        }, tx, cancellationToken: ct));
}
