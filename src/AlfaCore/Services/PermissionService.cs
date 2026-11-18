using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Regla única de permisos web (menú y pantallas usan la misma):
/// <list type="bullet">
/// <item>Opciones del menú y permisos de acción comunes: si el usuario no tiene ningún permiso web cargado ve
/// todo; si tiene alguno, solo lo tildado (propio o de su perfil).</item>
/// <item>Permisos explícitos (<see cref="PermisosWebCatalogo"/>): solo si están tildados; no se dan por "ve todo"
/// y viven en otra tabla, así tildarlos no le esconde el resto del menú.</item>
/// </list>
/// Si no se pueden leer los permisos, se niega (nunca se interpreta un error como acceso libre).
/// </summary>
public sealed class PermissionService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IPermissionService
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlySet<string>?> GetAllowedTaskKeysAsync(CancellationToken ct = default)
    {
        var estado = await LoadAsync(ct);
        if (estado is null)
            return null;

        return estado.Value.Ok ? estado.Value.Keys : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> HasAccessAsync(string clave, CancellationToken ct = default)
    {
        var key = (clave ?? string.Empty).Trim().ToUpperInvariant();
        var accion = PermisosWebCatalogo.Buscar(key);
        if (accion?.Explicito == true)
            return await HasExplicitAccessAsync(accion, ct);

        var estado = await LoadAsync(ct);
        return ResolveAccess(estado, key);
    }

    /// <summary>Decisión pura (sin base) para opciones comunes; la usa <see cref="HasAccessAsync"/> y los tests.</summary>
    internal static bool ResolveAccess((bool Ok, IReadOnlySet<string> Keys)? estado, string key)
    {
        if (estado is null)
            return true; // sin sesión de usuario o base sin tablas web: el control lo hace cada flujo
        if (!estado.Value.Ok)
            return false;
        // Sin ningún permiso web cargado = ve todo (igual que el menú).
        return estado.Value.Keys.Count == 0 || estado.Value.Keys.Contains(key);
    }

    private async Task<(bool Ok, IReadOnlySet<string> Keys)?> LoadAsync(CancellationToken ct)
    {
        try
        {
            var userName = appUserSession.CurrentUser?.UserName?.Trim();
            var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            if (!await TableExistsAsync(cn, "ALFACORE_TAREAS_WEB", ct))
                return null;

            const string sql = """
                SELECT DISTINCT UPPER(LTRIM(RTRIM(Clave)))
                FROM dbo.ALFACORE_TAREAS_WEB
                WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario
                  AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(Clave, '') <> '';
                """;

            var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant()
            }, commandTimeout: 5, cancellationToken: ct));

            IReadOnlySet<string> keys = rows
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return (true, keys);
        }
        catch (Exception ex)
        {
            await TryLogWarningAsync("Shell", "GetAllowedTaskKeys", ex,
                "No se pudieron resolver los permisos del usuario para el shell web.", ct);
            return (false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private async Task<bool> HasExplicitAccessAsync(PermisoWebAccion accion, CancellationToken ct)
    {
        var user = appUserSession.CurrentUser;
        if (user is null)
            return false;
        if (user.SuperAdmin)
            return true;

        var userName = user.UserName?.Trim() ?? string.Empty;
        var systemCode = user.SystemCode?.Trim() ?? string.Empty;
        if (userName.Length == 0 || systemCode.Length == 0)
            return false;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            return await TieneExplicitoAsync(cn, accion, userName, systemCode, ct);
        }
        catch (Exception ex)
        {
            await TryLogWarningAsync("Shell", "HasExplicitAccess", ex,
                "No se pudo verificar un permiso del usuario.", ct);
            return false;
        }
    }

    /// <summary>
    /// ¿El usuario tiene tildado este permiso explícito? Cada consulta va en su propia sentencia detrás
    /// de un IF OBJECT_ID: si la base todavía no corrió la actualización, devuelve false sin error.
    /// </summary>
    internal static async Task<bool> TieneExplicitoAsync(SqlConnection cn, PermisoWebAccion accion, string usuario, string sistema, CancellationToken ct, SqlTransaction? tx = null)
    {
        var sql = accion.Almacen == PermisoWebAlmacen.ConversacionesVerTodos
            ? """
              DECLARE @r bit = 0;
              IF OBJECT_ID(N'dbo.CONV_ADMINISTRADORES', N'U') IS NOT NULL
                  IF EXISTS (SELECT 1 FROM dbo.CONV_ADMINISTRADORES
                             WHERE UPPER(LTRIM(RTRIM(ISNULL(Usuario, N'')))) = @Usuario
                               AND UPPER(LTRIM(RTRIM(ISNULL(Sistema, N'')))) = @Sistema)
                      SET @r = 1;
              SELECT @r;
              """
            : """
              DECLARE @r bit = 0;
              IF OBJECT_ID(N'dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS', N'U') IS NOT NULL
                  IF EXISTS (SELECT 1 FROM dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS
                             WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                               AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                               AND UPPER(LTRIM(RTRIM(Clave))) = @Clave)
                      SET @r = 1;
              SELECT @r;
              """;

        return await cn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new
        {
            Usuario = usuario.Trim().ToUpperInvariant(),
            Sistema = sistema.Trim().ToUpperInvariant(),
            Clave = accion.Clave.ToUpperInvariant()
        }, tx, commandTimeout: 5, cancellationToken: ct));
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.tables
            WHERE object_id = OBJECT_ID(@FullName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { FullName = $"dbo.{tableName}" }, commandTimeout: 3, cancellationToken: ct));
        return count > 0;
    }

    private async Task TryLogWarningAsync(
        string process,
        string action,
        Exception exception,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            await appEvents.LogErrorAsync(
                process,
                action,
                exception,
                userMessage,
                new { User = appUserSession.CurrentUser?.UserName, System = appUserSession.CurrentUser?.SystemCode },
                severity: AppEventSeverity.Warning,
                ct: timeoutCts.Token);
        }
        catch
        {
            // La prioridad es no bloquear el shell por un fallo de permisos o de logging.
        }
    }
}
