using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface ILegacyBaseUserSessionService
{
    string? CurrentToken { get; }
    Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default);
    Task<AppUserSessionInfo> LoginAsync(string userName, string password, string connectionString, Guid? sessionId = null, CancellationToken ct = default);
    Task<AppUserSessionInfo?> ResolveByCentralLoginAsync(string loginCentral, CancellationToken ct = default);
    Task<IReadOnlyList<UsuarioSistemaDto>> GetLoginUsersAsync(CancellationToken ct = default);
}

public sealed class LegacyBaseUserSessionService(
    ISessionService sessionService,
    UsuariosPasswordCodec passwordCodec,
    AppUserSessionStore sessionStore,
    IAppEventService appEvents) : ILegacyBaseUserSessionService
{
    private AppUserSessionInfo? _currentUser;
    private string? _sessionToken;

    public string? CurrentToken => _sessionToken;

    public async Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default)
        => await LoginAsync(userName, password, GetActiveConnectionString(), sessionService.GetActiveSession()?.Id, ct);

    public async Task<AppUserSessionInfo> LoginAsync(string userName, string password, string connectionString, Guid? sessionId = null, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("Ingresá el usuario del sistema.");

            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Ingresá la contraseña del sistema.");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("No hay una sesión SQL activa seleccionada.");

            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);

            var hasActivo = await HasActivoColumnAsync(cn, ct);
            var byEmail = userName.Contains('@');
            var row = await LoadUserRowAsync(cn, userName.Trim(), byEmail, hasActivo, ct)
                ?? throw new InvalidOperationException("El usuario no existe en la base activa.");

            if (!row.Activo)
                throw new InvalidOperationException("El usuario está inactivo en la base activa.");

            if (row.EsGrupo)
                throw new InvalidOperationException("Los grupos no pueden iniciar sesión en AlfaCore.");

            var encodedCandidate = passwordCodec.Encode(password.Trim());
            var decodedStored = passwordCodec.Decode(row.Password);
            var passwordMatches =
                string.Equals(row.Password.Trim(), encodedCandidate, StringComparison.Ordinal) ||
                string.Equals(decodedStored, password.Trim(), StringComparison.Ordinal);

            if (!passwordMatches)
                throw new InvalidOperationException("La contraseña del sistema no es válida.");

            _currentUser = new AppUserSessionInfo
            {
                UserName = row.Nombre,
                Email = row.Email,
                CentralLogin = row.Nombre,
                SystemCode = row.Sistema,
                LoginAt = DateTime.Now,
                RequiresInternalLogin = false,
                AuthorizedSessionId = sessionId
            };

            _sessionToken = sessionStore.Store(_currentUser);
            return _currentUser;
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (sqlEx.Class >= 20 || sqlEx.Number is 53 or 2 or -2 or 258)
        {
            var servidor = TryGetDataSource(connectionString);
            var msg = string.IsNullOrWhiteSpace(servidor)
                ? "No se pudo conectar al servidor de base de datos. Verificá que el servidor esté encendido y accesible en la red."
                : $"No se pudo conectar al servidor de base de datos ({servidor}). Verificá que el servidor esté encendido y accesible en la red.";

            var incidentId = await TryLogErrorAsync("Central", "LoginInterno", sqlEx, msg, new { Usuario = userName.Trim() }, ct);
            throw new AppUserFacingException(msg, incidentId, sqlEx);
        }
        catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (sqlEx.Number is 18456 or 4060)
        {
            var servidor = TryGetDataSource(connectionString);
            var msg = string.IsNullOrWhiteSpace(servidor)
                ? "No se pudo autenticar contra la base seleccionada. RevisÃ¡ el usuario y la contraseÃ±a SQL configurados para esa base."
                : $"No se pudo autenticar contra la base seleccionada ({servidor}). RevisÃ¡ el usuario y la contraseÃ±a SQL configurados para esa base.";

            var incidentId = await TryLogErrorAsync("Central", "LoginInterno", sqlEx, msg, new { Usuario = userName.Trim() }, ct);
            throw new AppUserFacingException(msg, incidentId, sqlEx);
        }
        catch (Exception ex)
        {
            var incidentId = await TryLogErrorAsync(
                "Central",
                "LoginInterno",
                ex,
                "No se pudo validar el acceso interno.",
                new { Usuario = userName.Trim() },
                ct);

            throw new AppUserFacingException("No se pudo validar el acceso interno.", incidentId, ex);
        }
    }

    private static string TryGetDataSource(string connectionString)
    {
        try { return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).DataSource; }
        catch { return string.Empty; }
    }

    public async Task<AppUserSessionInfo?> ResolveByCentralLoginAsync(string loginCentral, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(loginCentral))
                return null;

            if (sessionService.GetActiveSession() is null)
                return null;

            await using var cn = new SqlConnection(GetActiveConnectionString());
            await cn.OpenAsync(ct);

            var hasActivo = await HasActivoColumnAsync(cn, ct);
            var byEmail = loginCentral.Contains('@');
            var row = await LoadUserRowAsync(cn, loginCentral.Trim(), byEmail, hasActivo, ct);
            if (row is null || row.EsGrupo || !row.Activo)
                return null;

            return new AppUserSessionInfo
            {
                UserName = row.Nombre,
                Email = row.Email,
                CentralLogin = loginCentral.Trim(),
                SystemCode = row.Sistema,
                LoginAt = DateTime.Now,
                RequiresInternalLogin = false,
                AuthorizedSessionId = sessionService.GetActiveSession()?.Id
            };
        }
        catch (Exception ex)
        {
            await TryLogErrorAsync(
                "Central",
                "ResolveInternalUser",
                ex,
                "No se pudo resolver el usuario interno.",
                new
                {
                    LoginCentral = loginCentral,
                    SesionSql = sessionService.GetActiveSession()?.Nombre
                },
                ct);

            return null;
        }
    }

    public async Task<IReadOnlyList<UsuarioSistemaDto>> GetLoginUsersAsync(CancellationToken ct = default)
    {
        try
        {
            if (sessionService.GetActiveSession() is null)
                return [];

            await using var cn = new SqlConnection(GetActiveConnectionString());
            await cn.OpenAsync(ct);

            var hasActivo = await HasActivoColumnAsync(cn, ct);
            var items = await LoadLoginUsersAsync(cn, hasActivo, "CN000PR", ct);
            if (items.Count == 0)
                items = await LoadLoginUsersAsync(cn, hasActivo, null, ct);

            return items;
        }
        catch (Exception ex)
        {
            await TryLogErrorAsync(
                "Central",
                "GetLoginUsers",
                ex,
                "No se pudo cargar el listado de usuarios del sistema.",
                new { SesionSql = sessionService.GetActiveSession()?.Nombre },
                ct);

            return [];
        }
    }

    private static async Task<List<UsuarioSistemaDto>> LoadLoginUsersAsync(SqlConnection cn, bool hasActivo, string? sistema, CancellationToken ct)
    {
        var sql = $"""
            SELECT DISTINCT
                ISNULL(NOMBRE, '') AS Nombre,
                ISNULL(SISTEMA, '') AS Sistema,
                {(hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)")} AS Activo,
                ISNULL(Administrador, 0) AS Administrador,
                ISNULL(EsGrupo, 0) AS EsGrupo
            FROM dbo.TA_USUARIOS
            WHERE LTRIM(RTRIM(ISNULL(NOMBRE, ''))) <> ''
              AND {(hasActivo ? "ISNULL(Activo, 1) = 1 AND " : string.Empty)}ISNULL(EsGrupo, 0) = 0
              {(string.IsNullOrWhiteSpace(sistema) ? string.Empty : "AND UPPER(LTRIM(RTRIM(ISNULL(SISTEMA, '')))) = UPPER(LTRIM(RTRIM(@Sistema)))")}
            ORDER BY NOMBRE;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        if (!string.IsNullOrWhiteSpace(sistema))
            cmd.Parameters.AddWithValue("@Sistema", sistema.Trim());

        await using var rd = await cmd.ExecuteReaderAsync(ct);

        var items = new List<UsuarioSistemaDto>();
        while (await rd.ReadAsync(ct))
        {
            items.Add(new UsuarioSistemaDto
            {
                Nombre = GetString(rd, 0),
                Sistema = GetString(rd, 1),
                Activo = GetBool(rd, 2),
                Administrador = GetBool(rd, 3),
                EsGrupo = GetBool(rd, 4)
            });
        }

        return items;
    }

    private async Task<UserRow?> LoadUserRowAsync(SqlConnection cn, string login, bool byEmail, bool hasActivo, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                ISNULL(NOMBRE, '') AS Nombre,
                ISNULL(PASSWORD, '') AS Password,
                ISNULL(email_de, '') AS Email,
                ISNULL(SISTEMA, '') AS Sistema,
                ISNULL(EsGrupo, 0) AS EsGrupo,
                {(hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)")} AS Activo
            FROM dbo.TA_USUARIOS
            WHERE {(byEmail ? "LOWER(LTRIM(RTRIM(ISNULL(email_de, ''))))" : "LOWER(LTRIM(RTRIM(NOMBRE)))")} = LOWER(LTRIM(RTRIM(@Login)))
              {(hasActivo ? "AND ISNULL(Activo, 1) = 1" : string.Empty)}
            ORDER BY NOMBRE;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Login", login);
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        if (!await rd.ReadAsync(ct))
            return null;

        return ReadUserRow(rd);
    }

    private static UserRow ReadUserRow(SqlDataReader rd)
        => new()
        {
            Nombre = GetString(rd, 0),
            Password = GetString(rd, 1),
            Email = GetString(rd, 2),
            Sistema = GetString(rd, 3),
            EsGrupo = GetBool(rd, 4),
            Activo = GetBool(rd, 5)
        };

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

    private string GetActiveConnectionString()
    {
        if (sessionService.GetActiveSession() is null)
            throw new InvalidOperationException("No hay una sesión SQL activa seleccionada.");

        var connectionString = sessionService.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("No hay una sesión SQL activa seleccionada.");

        return connectionString;
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index));

    private async Task<string> TryLogErrorAsync(
        string module,
        string action,
        Exception exception,
        string userMessage,
        object? data,
        CancellationToken ct)
    {
        try
        {
            using var logCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            logCts.CancelAfter(TimeSpan.FromSeconds(3));
            return await appEvents.LogErrorAsync(
                module,
                action,
                exception,
                userMessage,
                data,
                AppEventSeverity.Warning,
                logCts.Token);
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private sealed class UserRow
    {
        public string Nombre { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Sistema { get; init; } = string.Empty;
        public bool EsGrupo { get; init; }
        public bool Activo { get; init; }
    }
}
