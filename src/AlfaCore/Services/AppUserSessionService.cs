using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class AppUserSessionService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    UsuariosPasswordCodec passwordCodec,
    AppUserSessionStore sessionStore) : IAppUserSessionService
{
    private const string SistemaFijo = "CN000PR";
    private AppUserSessionInfo? _currentUser;
    private string? _sessionToken;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public event Action? StateChanged;

    public bool IsAuthenticated => _currentUser is not null;
    public AppUserSessionInfo? CurrentUser => _currentUser;
    public string? CurrentToken => _sessionToken;

    public async Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("Ingresá el usuario del sistema.");

            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Ingresá la contraseña del sistema.");

            var activeSqlSession = sessionService.GetActiveSession()
                ?? throw new InvalidOperationException("No hay una sesión SQL activa seleccionada.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            var hasActivo = await HasActivoColumnAsync(cn, ct);
            var sql = $"""
                SELECT
                    ISNULL(NOMBRE, ''),
                    ISNULL(PASSWORD, ''),
                    ISNULL(email_de, ''),
                    ISNULL(EsGrupo, 0),
                    {(hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)")}
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Nombre;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Sistema", SistemaFijo);
            cmd.Parameters.AddWithValue("@Nombre", userName.Trim().ToUpperInvariant());
            await using var rd = await cmd.ExecuteReaderAsync(ct);

            if (!await rd.ReadAsync(ct))
                throw new InvalidOperationException("El usuario no existe en la base activa.");

            var canonicalUser = GetString(rd, 0);
            var storedPassword = GetString(rd, 1);
            var email = GetString(rd, 2);
            var esGrupo = GetBool(rd, 3);
            var activo = GetBool(rd, 4);

            if (!activo)
                throw new InvalidOperationException("El usuario está inactivo en la base activa.");

            if (esGrupo)
                throw new InvalidOperationException("Los grupos no pueden iniciar sesión en AlfaCore.");

            var encodedCandidate = passwordCodec.Encode(password.Trim());
            var decodedStored = passwordCodec.Decode(storedPassword);
            var passwordMatches =
                string.Equals(storedPassword.Trim(), encodedCandidate, StringComparison.Ordinal) ||
                string.Equals(decodedStored, password.Trim(), StringComparison.Ordinal);

            if (!passwordMatches)
                throw new InvalidOperationException("La contraseña del sistema no es válida.");

            _currentUser = new AppUserSessionInfo
            {
                UserName = canonicalUser,
                Email = email,
                SystemCode = SistemaFijo,
                LoginAt = DateTime.Now,
                SqlSessionId = activeSqlSession.Id,
                SqlSessionName = activeSqlSession.Nombre
            };

            _sessionToken = sessionStore.Store(_currentUser);
            StateChanged?.Invoke();
            return _currentUser;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            var activeSqlSession = sessionService.GetActiveSession();
            var friendlyMessage = BuildKnownSqlLoginMessage(ex, activeSqlSession);

            var incidentId = await appEvents.LogErrorAsync(
                "Seguridad",
                "Login",
                ex,
                friendlyMessage,
                new
                {
                    UserName = userName?.Trim(),
                    Sistema = SistemaFijo,
                    SqlSession = activeSqlSession?.Nombre,
                    SqlServer = activeSqlSession?.Servidor,
                    SqlDatabase = activeSqlSession?.BaseDatos,
                    SqlUser = activeSqlSession?.Usuario
                },
                AppEventSeverity.Warning,
                ct);

            throw new InvalidOperationException($"{friendlyMessage} Código: {incidentId}", ex);
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                "Seguridad",
                "Login",
                ex,
                "No se pudo validar el usuario en la base activa.",
                new
                {
                    UserName = userName?.Trim(),
                    Sistema = SistemaFijo,
                    SqlSession = sessionService.GetActiveSession()?.Nombre
                },
                AppEventSeverity.Warning,
                ct);

            throw new InvalidOperationException($"No se pudo validar el usuario en la base activa. Código: {incidentId}", ex);
        }
    }

    public bool TryRestoreFromToken(string token)
    {
        if (!sessionStore.TryGet(token, out var info) || info is null)
            return false;

        var activeSqlSession = sessionService.GetActiveSession();
        if (activeSqlSession is null || activeSqlSession.Id != info.SqlSessionId)
        {
            sessionStore.Remove(token);
            return false;
        }

        _sessionToken = token;
        _currentUser = info;
        StateChanged?.Invoke();
        return true;
    }

    public void Logout()
    {
        if (_currentUser is null)
            return;

        if (_sessionToken is not null)
        {
            sessionStore.Remove(_sessionToken);
            _sessionToken = null;
        }

        _currentUser = null;
        StateChanged?.Invoke();
    }

    public void HandleSqlSessionChanged()
    {
        if (_currentUser is null)
            return;

        var activeSqlSession = sessionService.GetActiveSession();
        if (activeSqlSession is null || activeSqlSession.Id != _currentUser.SqlSessionId)
        {
            if (_sessionToken is not null)
            {
                sessionStore.Remove(_sessionToken);
                _sessionToken = null;
            }

            _currentUser = null;
            StateChanged?.Invoke();
        }
    }

    public string GetCurrentUserName(string fallback = "")
        => _currentUser?.UserName ?? fallback;

    private static async Task<bool> HasActivoColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_USUARIOS')
              AND LOWER(name) = N'activo';
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index));

    private static string BuildKnownSqlLoginMessage(SqlException ex, SessionDto? session)
    {
        var db = string.IsNullOrWhiteSpace(session?.BaseDatos) ? "la base seleccionada" : $"la base {session.BaseDatos}";
        var server = string.IsNullOrWhiteSpace(session?.Servidor) ? "el servidor configurado" : $"el servidor {session.Servidor}";
        var sqlUser = string.IsNullOrWhiteSpace(session?.Usuario) ? "el usuario SQL configurado" : $"el usuario SQL {session.Usuario}";

        return ex.Number switch
        {
            4060 => $"No se pudo abrir {db} en {server}. Revisá que la sesión SQL activa apunte a una base existente y que {sqlUser} tenga acceso.",
            18456 => $"No se pudo iniciar sesión en SQL Server para entrar a {db}. Revisá el usuario y la contraseña SQL guardados en la sesión activa.",
            53 or -1 or 2 => $"No se pudo conectar a {server}. Revisá el nombre del servidor, la red y que SQL Server esté accesible desde AlfaCore.",
            -2 => $"La conexión a {server} tardó demasiado y expiró. Revisá conectividad, carga del servidor o credenciales de la sesión activa.",
            _ => $"No se pudo conectar a {db} para validar el usuario del sistema. Revisá la sesión SQL activa y la conectividad del servidor."
        };
    }
}
