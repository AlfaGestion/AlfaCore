using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Convierte un rechazo de SQL Server al grabar un comprobante en un mensaje que diga qué campo y
/// qué tabla fallaron (como informa el legacy), para que el usuario sepa qué corregir.
/// Sirve con el servidor en inglés o en castellano.
/// </summary>
public static partial class CargaComprobantesErroresSql
{
    public static string Explicar(SqlException ex)
    {
        var msg = ex.Message.Trim();
        var tabla = Tabla(msg);
        var columna = Columna(msg);
        var donde = (columna, tabla) switch
        {
            ({ } c, { } t) => $"el campo {c} de {t}",
            ({ } c, null) => $"el campo {c}",
            (null, { } t) => $"la tabla {t}",
            _ => null
        };
        return ex.Number switch
        {
            // RAISERROR/THROW de triggers y procedimientos: el texto ya es para el usuario.
            >= 50000 => $"La base rechazó la grabación: {msg}",
            2628 or 8152 => $"Un dato es más largo de lo que admite {donde ?? "la base"}"
                + (Valor(msg) is { } v ? $" (valor: «{v}»)" : "") + ". Acortalo y volvé a validar.",
            515 => $"Falta un dato obligatorio: {donde ?? msg}. Completalo y volvé a validar.",
            547 => $"Un código no existe o no es válido en {donde ?? "la base"}"
                + (Restriccion(msg) is { } r ? $" (regla {r})" : "") + ". Revisá cliente, artículos, depósito, cuentas y vendedor.",
            2627 or 2601 => $"Ya existe un registro con esa clave en {tabla ?? "la base"}"
                + (Valor(msg) is { } d ? $" ({d})" : "") + ". Revisá la numeración del talonario.",
            1205 => "Otra terminal estaba grabando al mismo tiempo. Volvé a intentar.",
            -2 => "La base tardó demasiado en responder. Volvé a intentar.",
            245 or 8114 or 8115 => $"Un dato tiene un formato o tamaño que la base no acepta: {msg}",
            _ => $"La base rechazó la grabación (error SQL {ex.Number}): {msg}"
        };
    }

    private static string? Tabla(string msg)
    {
        var m = TablaRegex().Match(msg);
        if (!m.Success) return null;
        var nombre = m.Groups[1].Value;
        // 'base.dbo.TABLA' o "dbo.TABLA": solo el nombre de la tabla.
        return nombre[(nombre.LastIndexOf('.') + 1)..];
    }

    private static string? Columna(string msg)
        => ColumnaRegex().Match(msg) is { Success: true } m ? m.Groups[1].Value : null;

    private static string? Valor(string msg)
        => ValorRegex().Match(msg) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    private static string? Restriccion(string msg)
        => RestriccionRegex().Match(msg) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex("""(?:table|tabla)\s+['"]([^'"]+)['"]""", RegexOptions.IgnoreCase)]
    private static partial Regex TablaRegex();

    [GeneratedRegex("""(?:column|columna)\s+['"]([^'"]+)['"]""", RegexOptions.IgnoreCase)]
    private static partial Regex ColumnaRegex();

    [GeneratedRegex("""(?:Truncated value|valor truncado|duplicate key value is|valor de clave duplicada es)\s*:?\s*['(]?([^')]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ValorRegex();

    [GeneratedRegex("""(?:constraint|restricci[oó]n)\s+(?:FOREIGN KEY|CHECK|REFERENCE|de clave externa|CHECK)?\s*['"]([^'"]+)['"]""", RegexOptions.IgnoreCase)]
    private static partial Regex RestriccionRegex();
}
