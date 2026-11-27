using System.Collections.Concurrent;
using AlfaCore.Mcp.Infra;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Mcp.Auth;

/// <summary>
/// Multi-base: la base a consultar sale del token (claim "base" = ALFA_CENTRAL.bases.id), nunca de lo que pide el modelo.
/// - Token con base: servidor y nombre de ALFA_CENTRAL.bases + login de SOLO LECTURA registrado para esa base (MCP_BASE, clave cifrada).
///   Si la base o el acceso del mail se dieron de baja, se corta en menos de <see cref="Vigencia"/>. NUNCA cae a otra base.
/// - Sin base (usuarios con clave sin base asignada, y los endpoints OAuth antes del login): la base por defecto (ALFACORE_MCP_CONNECTION),
///   que es ademas la que tiene la configuracion OAuth (IA_MCP_OAUTH_*).
/// </summary>
public sealed class ConexionPorBaseResolver(
    IHttpContextAccessor accessor, IAccesoStore accesos, SecretProtector protector, string conexionPorDefecto, ILogger<ConexionPorBaseResolver> log)
    : IConexionResolver
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(int IdBase, string? Email), (DateTime Hasta, string? Conexion, string? Error)> _cache = new();

    public const string ClaimBase = "base";
    public const string ClaimMetodo = "amr";
    public const string MetodoEmail = "email";

    public async Task<string> GetConnectionStringAsync(CancellationToken ct)
    {
        var user = accessor.HttpContext?.User;
        var claimBase = user?.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimBase)?.Value : null;
        if (string.IsNullOrEmpty(claimBase)) return conexionPorDefecto;
        if (!int.TryParse(claimBase, out var idBase))
            throw new ToolUserException("La sesión tiene una base inválida. Desconectá y volvé a conectar el conector.");

        // Los usuarios que entraron por mail se revalidan contra MCP_ACCESO: dar de baja el acceso corta las consultas enseguida.
        var email = user!.FindFirst(ClaimMetodo)?.Value == MetodoEmail ? user.FindFirst("preferred_username")?.Value : null;
        var clave = (idBase, email);
        if (!_cache.TryGetValue(clave, out var hit) || hit.Hasta <= DateTime.UtcNow)
        {
            hit = await ResolverAsync(idBase, email, ct);
            _cache[clave] = hit;
        }
        return hit.Conexion ?? throw new ToolUserException(hit.Error!);
    }

    private async Task<(DateTime, string?, string?)> ResolverAsync(int idBase, string? email, CancellationToken ct)
    {
        var hasta = DateTime.UtcNow + Vigencia;
        var b = await accesos.GetBaseAsync(idBase, ct);
        if (b is not { Habilitada: true })
            return (hasta, null, "Esta base no está habilitada para consultas con IA. Consultá con ALFA NET.");
        if (email is not null && !(await accesos.AccesosAsync(email, ct)).Any(a => a.IdBase == idBase))
            return (hasta, null, "Tu acceso a esta base fue dado de baja. Consultá con ALFA NET.");
        try
        {
            return (hasta, new SqlConnectionStringBuilder
            {
                DataSource = b.Servidor,
                InitialCatalog = b.BaseDatos,
                UserID = b.UsuarioSql,
                Password = string.IsNullOrEmpty(b.ClaveCifrada) ? "" : protector.Revelar(b.ClaveCifrada),
                TrustServerCertificate = true,
                Encrypt = false,
                ApplicationName = "AlfaCore.Mcp",
                ConnectTimeout = 15
            }.ConnectionString, null);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            log.LogError(ex, "No se pudo descifrar la clave SQL de la base {IdBase} (clave de cifrado cambiada?).", idBase);
            return (hasta, null, "La conexión de esta base quedó mal configurada. Consultá con ALFA NET.");
        }
    }
}
