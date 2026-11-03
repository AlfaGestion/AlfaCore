using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Reglas de ambiente para emitir comprobantes desde AlfaCore:
/// - Base de prueba (el nombre de la base o de la conexión contiene PRUEBA o DEMO): siempre homologación,
///   producción bloqueada.
/// - Base real: solo emite si se habilitó «Emitir comprobantes reales desde AlfaCore»
///   (TA_CONFIGURACION ALFACORE_EMISION_REAL=SI) y ARCA está en producción. Nunca se graban comprobantes
///   de homologación en una base real.
/// </summary>
public static class ArcaModoEmision
{
    public const string ClaveEmisionReal = "ALFACORE_EMISION_REAL";
    public const string Homologacion = "HOMOLOGACION";
    public const string Produccion = "PRODUCCION";
    public const string Deshabilitada = "DESHABILITADA";

    public static bool EsBasePrueba(params string?[] nombres)
        => nombres.Any(n => !string.IsNullOrWhiteSpace(n) &&
            (n.Contains("PRUEBA", StringComparison.OrdinalIgnoreCase) || n.Contains("DEMO", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Nombre de la base activa (alias de la conexión); vacío si no hay sesión.</summary>
    public static string NombreSesion(ISessionService sessions)
    {
        try { return sessions.GetActiveSession()?.Nombre ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>Modo efectivo de la base: HOMOLOGACION, PRODUCCION o DESHABILITADA (con el motivo).</summary>
    public static async Task<(string Modo, string Motivo)> LeerAsync(SqlConnection cn, string? nombreSesion, CancellationToken ct, System.Data.IDbTransaction? tx = null)
    {
        if (EsBasePrueba(cn.Database, nombreSesion))
            return (Homologacion, "Base de prueba: se factura siempre en homologación, sin validez fiscal.");
        var valores = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,'')))
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'ARCA_AMBIENTE', N'ALFACORE_EMISION_REAL');
            """, transaction: tx, cancellationToken: ct)))
            .GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(valores.GetValueOrDefault(ClaveEmisionReal), "SI", StringComparison.OrdinalIgnoreCase))
            return (Deshabilitada, "Esta base no está habilitada para emitir comprobantes reales desde AlfaCore. " +
                "Se activa en Configuración General → Ventas → Facturación electrónica.");
        if (!string.Equals(valores.GetValueOrDefault("ARCA_AMBIENTE"), Produccion, StringComparison.OrdinalIgnoreCase))
            return (Deshabilitada, "La emisión real está habilitada pero ARCA está configurado en homologación. " +
                "En una base real hay que poner el ambiente en Producción (Configuración General → Ventas → Facturación electrónica).");
        return (Produccion, "Emisión real: los comprobantes se autorizan en ARCA producción y tienen validez fiscal.");
    }
}
