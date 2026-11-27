using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AlfaCore.Mcp.Auth;

/// <summary>Configuracion de infraestructura del servidor OAuth (appsettings / variables de entorno; no es configuracion de negocio).</summary>
public sealed record AuthOptions(
    bool Enabled,
    string Store,
    string Issuer,
    string EndpointPath,
    string SigningKeyPath,
    string? DevUser,
    string? DevPassword,
    bool DevTotp,
    string? DevTotpSecret,
    int LimiteLoginPorMin,
    int LimiteTokenPorMin,
    int LimiteRegistroPorMin)
{
    public const string Scope = "mcp:read";

    /// <summary>URL publica del recurso MCP (audience de los tokens): Issuer + EndpointPath.</summary>
    public string Resource => Issuer + EndpointPath;

    public static AuthOptions Load(IConfiguration cfg, IHostEnvironment env)
    {
        var s = cfg.GetSection("Mcp:Auth");
        var dev = env.IsDevelopment();
        // Produccion: SIEMPRE prendida. Development: apagada salvo que se la prenda (Mcp__Auth__Enabled=true) para probar el flujo OAuth.
        var enabled = s.GetValue<bool?>("Enabled") ?? !dev;
        if (!enabled && !dev)
            throw new InvalidOperationException("Mcp:Auth:Enabled=false solo se permite en el entorno Development.");

        var store = s["Store"] ?? "Sql";
        if (store.Equals("Memory", StringComparison.OrdinalIgnoreCase) && !dev)
            throw new InvalidOperationException("Mcp:Auth:Store=Memory solo se permite en el entorno Development.");

        var issuer = (s["Issuer"] ?? "").Trim().TrimEnd('/');
        if (enabled)
        {
            if (!Uri.TryCreate(issuer, UriKind.Absolute, out var u))
                throw new InvalidOperationException("Falta Mcp:Auth:Issuer: URL publica de esta aplicacion (ej. https://alfacentral.ddns.net/ia).");
            if (u.Scheme != Uri.UriSchemeHttps && !(dev && u.IsLoopback))
                throw new InvalidOperationException("Mcp:Auth:Issuer debe ser https (http solo en Development sobre localhost).");
        }

        var signingKeyPath = s["SigningKeyPath"];
        // Produccion: ruta absoluta obligatoria. Con la ruta por defecto (relativa a la carpeta de la app) el comando crear-usuario,
        // corrido desde otra carpeta o sin la variable, crearia OTRA clave TOTP (*.totp-key) y el sitio no podria leer los secretos.
        if (enabled && !dev && (string.IsNullOrWhiteSpace(signingKeyPath) || !Path.IsPathRooted(signingKeyPath)))
            throw new InvalidOperationException(@"Falta Mcp:Auth:SigningKeyPath con ruta absoluta fuera de la carpeta publicada (ej. C:\ProgramData\AlfaCoreMcp\signing-key.pem). El sitio y el comando crear-usuario deben usar la MISMA ruta.");

        var endpoint = "/" + (s["EndpointPath"] ?? "/mcp").Trim('/');
        return new AuthOptions(
            enabled, store, issuer, endpoint,
            string.IsNullOrWhiteSpace(signingKeyPath) ? "App_Data/mcp-signing-key.pem" : signingKeyPath,
            s["DevUser"], s["DevPassword"], s.GetValue("DevTotp", false), s["DevTotpSecret"],
            s.GetValue("LimiteLoginPorMin", 10), s.GetValue("LimiteTokenPorMin", 60), s.GetValue("LimiteRegistroPorMin", 10));
    }
}

/// <summary>PBKDF2-HMAC-SHA256 con sal propia por usuario. Formato: pbkdf2-sha256$iteraciones$sal$hash (base64).</summary>
public static class PasswordHasher
{
    public const int Iteraciones = 600_000;
    public const int LargoMinimo = 12;
    private static readonly Lazy<string> HashFalso = new(() => Hash("clave-falsa-para-igualar-tiempos"));

    public static string Hash(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, sal, Iteraciones, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string clave, string almacenado)
    {
        var p = almacenado.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2-sha256" || !int.TryParse(p[1], out var iter) || iter < 10_000) return false;
        try
        {
            var sal = Convert.FromBase64String(p[2]);
            var esperado = Convert.FromBase64String(p[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(clave, sal, iter, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Gasta el mismo tiempo que una verificacion real, para no revelar por latencia si un usuario existe.</summary>
    public static void IgualarTiempo(string clave) => Verificar(clave, HashFalso.Value);
}

public static class Pkce
{
    private static readonly Regex Verifier = new("^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.Compiled);
    private static readonly Regex Challenge = new("^[A-Za-z0-9\\-_]{43}$", RegexOptions.Compiled);

    public static bool ChallengeValido(string? challenge) => challenge is not null && Challenge.IsMatch(challenge);

    public static bool VerificarS256(string? verifier, string challenge)
    {
        if (verifier is null || !Verifier.IsMatch(verifier)) return false;
        var calculado = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(calculado), Encoding.ASCII.GetBytes(challenge));
    }
}

public static class Secretos
{
    /// <summary>Token aleatorio de 256 bits en base64url.</summary>
    public static string NuevoToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lo unico que se guarda en la base de codigos y refresh tokens: SHA-256 en hexadecimal.</summary>
    public static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();
}

/// <summary>
/// Politica de redirect URIs. Un cliente solo puede registrar URIs que coincidan con la lista blanca (IA_MCP_OAUTH_REDIRECTS).
/// Las entradas pueden llevar puerto comodin solo en loopback: http://localhost:*/callback (RFC 8252).
/// </summary>
public static class RedirectPolicy
{
    public static bool Permitida(string? uri, IEnumerable<string> listaBlanca)
    {
        if (!Valida(uri, out var u)) return false;
        foreach (var entrada in listaBlanca)
        {
            if (entrada.Contains(":*", StringComparison.Ordinal))
            {
                // Comodin de puerto: solo para loopback.
                var rx = "^" + Regex.Escape(entrada).Replace(":\\*", ":\\d{1,5}") + "$";
                if (u!.IsLoopback && Regex.IsMatch(uri!, rx, RegexOptions.CultureInvariant)) return true;
            }
            else if (string.Equals(entrada, uri, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>La URI pedida en /authorize debe ser exactamente una de las registradas (en loopback se ignora el puerto).</summary>
    public static bool CoincideConRegistrada(string? pedida, IEnumerable<string> registradas)
    {
        if (!Valida(pedida, out var up)) return false;
        foreach (var r in registradas)
        {
            if (string.Equals(r, pedida, StringComparison.Ordinal)) return true;
            if (Uri.TryCreate(r, UriKind.Absolute, out var ur) && ur.IsLoopback && up!.IsLoopback
                && ur.Scheme == up.Scheme && string.Equals(ur.Host, up.Host, StringComparison.OrdinalIgnoreCase)
                && ur.AbsolutePath == up.AbsolutePath && ur.Query == up.Query) return true;
        }
        return false;
    }

    private static bool Valida(string? uri, out Uri? u)
    {
        u = null;
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > 2000 || !Uri.TryCreate(uri, UriKind.Absolute, out u)) return false;
        if (u.Fragment.Length > 0 || u.UserInfo.Length > 0) return false;
        return u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback);
    }
}

/// <summary>Par de claves RSA de firma. Se lee de un PEM fuera del repo; si no existe se genera. Nunca se versiona.</summary>
public sealed class SigningKeyProvider
{
    public RsaSecurityKey Key { get; }
    public JsonWebKey PublicJwk { get; }

    public SigningKeyProvider(AuthOptions opt, IHostEnvironment env)
    {
        var ruta = Path.IsPathRooted(opt.SigningKeyPath) ? opt.SigningKeyPath : Path.Combine(env.ContentRootPath, opt.SigningKeyPath);
        var rsa = RSA.Create(2048);
        if (File.Exists(ruta)) rsa.ImportFromPem(File.ReadAllText(ruta));
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, rsa.ExportPkcs8PrivateKeyPem());
        }
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16];
        Key = new RsaSecurityKey(rsa) { KeyId = kid };
        PublicJwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = kid });
        PublicJwk.Use = "sig";
        PublicJwk.Alg = SecurityAlgorithms.RsaSha256;
    }
}

/// <summary>Emite y valida los access tokens (JWT RS256, audience = URL del recurso MCP).</summary>
public sealed class TokenService(SigningKeyProvider keys, AuthOptions opt)
{
    /// <param name="baseId">Base de la autorizacion (claim "base"); null = base por defecto. <paramref name="metodo"/>: "pwd" o "email" (claim "amr").</param>
    public string CrearAccessToken(UsuarioAuth usuario, string clientId, string scope, TimeSpan vida, DateTime ahoraUtc, string? baseId = null, string metodo = "pwd")
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opt.Issuer,
            Audience = opt.Resource,
            IssuedAt = ahoraUtc,
            NotBefore = ahoraUtc,
            Expires = ahoraUtc + vida,
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = usuario.Id.ToString(),
                ["preferred_username"] = usuario.Usuario,
                ["name"] = usuario.Nombre,
                ["client_id"] = clientId,
                ["scope"] = scope,
                ["jti"] = Guid.NewGuid().ToString("N"),
                ["base"] = baseId ?? usuario.Base ?? "",
                ["amr"] = metodo
            }
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public TokenValidationParameters ParametrosValidacion() => new()
    {
        ValidateIssuer = true, ValidIssuer = opt.Issuer,
        ValidateAudience = true, ValidAudience = opt.Resource,
        ValidateIssuerSigningKey = true, IssuerSigningKey = keys.Key,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "preferred_username"
    };
}
