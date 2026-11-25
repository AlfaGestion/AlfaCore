using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AlfaCore.Mcp.Auth;

/// <summary>Base32 (RFC 4648, mayusculas, sin relleno): el formato que esperan las apps autenticadoras.</summary>
public static class Base32
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Codificar(byte[] datos)
    {
        var sb = new StringBuilder((datos.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in datos)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { sb.Append(Alfabeto[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alfabeto[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decodificar(string texto)
    {
        var salida = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in texto.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var v = Alfabeto.IndexOf(c);
            if (v < 0) throw new FormatException("Base32 invalido.");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8) { salida.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return salida.ToArray();
    }
}

/// <summary>TOTP (RFC 6238): HMAC-SHA1, 6 digitos, pasos de 30 s, ventana de +-1 paso para tolerar el desfase de reloj del telefono.</summary>
public static class Totp
{
    public const int Digitos = 6;
    public const int PeriodoSeg = 30;

    public static string NuevoSecreto() => Base32.Codificar(RandomNumberGenerator.GetBytes(20));

    public static long PasoDe(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds() / PeriodoSeg;

    public static string Codigo(string secretoBase32, long paso, int digitos = Digitos)
    {
        Span<byte> contador = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(contador, paso);
        var hmac = HMACSHA1.HashData(Base32.Decodificar(secretoBase32), contador);
        var offset = hmac[^1] & 0x0F;
        var binario = ((hmac[offset] & 0x7F) << 24) | (hmac[offset + 1] << 16) | (hmac[offset + 2] << 8) | hmac[offset + 3];
        var modulo = 1; for (var i = 0; i < digitos; i++) modulo *= 10;
        return (binario % modulo).ToString().PadLeft(digitos, '0');
    }

    /// <summary>Devuelve el paso que coincidio. El llamador debe registrar ese paso para que el mismo codigo no se pueda reusar (anti-replay).</summary>
    public static (bool Ok, long Paso) Verificar(string secretoBase32, string? codigo, DateTime ahoraUtc, int ventana = 1)
    {
        var limpio = (codigo ?? "").Replace(" ", "");
        if (limpio.Length != Digitos || !limpio.All(char.IsAsciiDigit)) return (false, 0);
        var actual = PasoDe(ahoraUtc);
        var ok = false; long pasoOk = 0;
        // Se recorren TODOS los pasos (sin cortar) para no filtrar por tiempo cual coincidio.
        for (var p = actual - ventana; p <= actual + ventana; p++)
        {
            var coincide = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Codigo(secretoBase32, p)), Encoding.ASCII.GetBytes(limpio));
            if (coincide) { ok = true; pasoOk = p; }
        }
        return (ok, pasoOk);
    }

    public static string UriOtpAuth(string secretoBase32, string cuenta, string emisor) =>
        $"otpauth://totp/{Uri.EscapeDataString(emisor)}:{Uri.EscapeDataString(cuenta)}?secret={secretoBase32}&issuer={Uri.EscapeDataString(emisor)}&algorithm=SHA1&digits={Digitos}&period={PeriodoSeg}";
}

/// <summary>
/// Cifra el secreto TOTP antes de guardarlo (AES-256-GCM). La clave vive en un archivo junto a la clave de firma, fuera del repo y
/// fuera de la base: quien lea solo la tabla MCP_USUARIO no obtiene secretos utilizables.
/// </summary>
public sealed class SecretProtector
{
    private readonly byte[] _clave;

    public SecretProtector(AuthOptions opt, IHostEnvironment env)
    {
        var ruta = (Path.IsPathRooted(opt.SigningKeyPath) ? opt.SigningKeyPath : Path.Combine(env.ContentRootPath, opt.SigningKeyPath)) + ".totp-key";
        if (File.Exists(ruta)) _clave = Convert.FromBase64String(File.ReadAllText(ruta).Trim());
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            _clave = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(ruta, Convert.ToBase64String(_clave));
        }
        if (_clave.Length != 32) throw new InvalidOperationException("La clave de cifrado TOTP debe tener 32 bytes.");
    }

    public string Proteger(string secreto)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var datos = Encoding.UTF8.GetBytes(secreto);
        var cifrado = new byte[datos.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_clave, 16);
        aes.Encrypt(nonce, datos, cifrado, tag);
        return "v1." + Convert.ToBase64String(nonce.Concat(tag).Concat(cifrado).ToArray());
    }

    public string Revelar(string protegido)
    {
        if (!protegido.StartsWith("v1.", StringComparison.Ordinal)) throw new CryptographicException("Formato de secreto desconocido.");
        var todo = Convert.FromBase64String(protegido[3..]);
        var nonce = todo[..12]; var tag = todo[12..28]; var cifrado = todo[28..];
        var datos = new byte[cifrado.Length];
        using var aes = new AesGcm(_clave, 16);
        aes.Decrypt(nonce, cifrado, tag, datos);
        return Encoding.UTF8.GetString(datos);
    }
}
