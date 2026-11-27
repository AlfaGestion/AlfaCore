using System.Security.Cryptography;
using System.Text;

namespace AlfaCore.Compartido;

/// <summary>
/// Clave del login de SOLO LECTURA de cada base habilitada para el MCP (ALFA_CENTRAL.MCP_BASE.ClaveCifrada).
/// Archivo COMPARTIDO: lo compilan AlfaCore (que crea el login y guarda la clave desde /admin) y AlfaCore.Mcp (que la lee).
/// Se cifra con DPAPI de la MAQUINA: AlfaCore y el MCP corren en el mismo servidor web, asi que ninguno necesita una clave
/// de cifrado extra. Consecuencia: la clave solo se puede leer en el servidor donde se habilito la base (habilitar desde el
/// /admin de produccion, no desde una PC de desarrollo).
/// </summary>
public static class ClaveBaseMcp
{
    public const string Prefijo = "dpapi:";
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("AlfaCore.Mcp/MCP_BASE/v1");

    // Sin comillas, corchetes ni caracteres que haya que escapar en T-SQL: la clave se puede embeber en CREATE LOGIN sin riesgo.
    private const string Mayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Minusculas = "abcdefghijkmnopqrstuvwxyz";
    private const string Digitos = "23456789";
    private const string Simbolos = "#%*+-=?@^_~";

    public static bool EstaCifradaConDpapi(string? valor) => valor is not null && valor.StartsWith(Prefijo, StringComparison.Ordinal);

    public static string Cifrar(string clave)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("El cifrado de claves del MCP requiere Windows (DPAPI).");
        var datos = ProtectedData.Protect(Encoding.UTF8.GetBytes(clave), Entropia, DataProtectionScope.LocalMachine);
        return Prefijo + Convert.ToBase64String(datos);
    }

    /// <exception cref="CryptographicException">Si se cifro en otra maquina.</exception>
    public static string Descifrar(string valor)
    {
        if (!EstaCifradaConDpapi(valor)) throw new ArgumentException("La clave no tiene el formato de DPAPI.", nameof(valor));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("El cifrado de claves del MCP requiere Windows (DPAPI).");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(valor[Prefijo.Length..]), Entropia, DataProtectionScope.LocalMachine));
    }

    /// <summary>Clave aleatoria de 32 caracteres con mayusculas, minusculas, digitos y simbolos (cumple CHECK_POLICY).</summary>
    public static string NuevaClave()
    {
        const string todos = Mayusculas + Minusculas + Digitos + Simbolos;
        var c = new char[32];
        c[0] = Mayusculas[RandomNumberGenerator.GetInt32(Mayusculas.Length)];
        c[1] = Minusculas[RandomNumberGenerator.GetInt32(Minusculas.Length)];
        c[2] = Digitos[RandomNumberGenerator.GetInt32(Digitos.Length)];
        c[3] = Simbolos[RandomNumberGenerator.GetInt32(Simbolos.Length)];
        for (var i = 4; i < c.Length; i++) c[i] = todos[RandomNumberGenerator.GetInt32(todos.Length)];
        RandomNumberGenerator.Shuffle(c.AsSpan());
        return new string(c);
    }

    /// <summary>Un login propio por base: si se filtrara su clave, solo abre esa base (y solo el esquema dir).</summary>
    public static string NombreLogin(int idBase) =>
        idBase > 0 ? "mcp_ro_" + idBase.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException(nameof(idBase));

    /// <summary>Solo caracteres seguros para usar entre corchetes y comillas en T-SQL dinamico.</summary>
    public static bool EsSeguroParaSql(string valor) =>
        valor.Length is > 0 and <= 128 && valor.All(ch => char.IsAsciiLetterOrDigit(ch) || "#%*+-=?@^_~".Contains(ch));
}
