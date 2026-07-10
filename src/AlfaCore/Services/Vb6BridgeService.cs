using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class Vb6BridgeService(
    IConfiguration configuration,
    ISessionService sessionService,
    ILegacyBaseUserSessionService legacyBaseUserSession,
    UsuariosPasswordCodec passwordCodec,
    Vb6BridgeTicketStore ticketStore) : IVb6BridgeService
{
    private const string SistemaFijo = "CN000PR";
    private static readonly TimeSpan TicketTtl = TimeSpan.FromMinutes(3);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<string> CreateTicketAsync(Vb6AuthTicketRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedModule = NormalizeModule(request.Modulo);
        if (string.IsNullOrWhiteSpace(normalizedModule))
            throw new InvalidOperationException("Ingresá el módulo a abrir.");

        var sqlSession = BuildSqlSession(request);
        await ValidateSqlConnectionAsync(sqlSession, ct);
        var user = await ValidateAppUserAsync(sqlSession.ConnectionString, request.UsuarioSistema, request.PasswordSistema, ct);

        return ticketStore.Create(new Vb6BridgeTicketRecord
        {
            ExpiresAt = DateTime.UtcNow.Add(TicketTtl),
            Servidor = sqlSession.Servidor,
            BaseDatos = sqlSession.BaseDatos,
            UsuarioSql = sqlSession.Usuario,
            PasswordSql = sqlSession.Password,
            UsuarioSistema = user.UserName,
            PasswordSistema = request.PasswordSistema.Trim(),
            Modulo = normalizedModule,
            NombreSesion = string.IsNullOrWhiteSpace(request.NombreSesion)
                ? null
                : request.NombreSesion.Trim()
        });
    }

    public async Task<Vb6ConsumeTicketResult> ConsumeTicketAsync(string ticket, CancellationToken ct = default)
    {
        if (!ticketStore.TryConsume(ticket, out var record) || record is null)
            throw new InvalidOperationException("El ticket no existe o ya expiró.");

        var session = EnsureSession(record);
        await LoginAppUserAsync(record, ct);
        var token = legacyBaseUserSession.CurrentToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("No se pudo crear la sesión del usuario del sistema.");

        return new Vb6ConsumeTicketResult
        {
            SqlSessionId = session.Id.ToString(),
            UserToken = token,
            RedirectUrl = $"/{record.Modulo}?directo=1"
        };
    }

    private SessionDto EnsureSession(Vb6BridgeTicketRecord record)
    {
        var existing = sessionService
            .GetAllSessions()
            .FirstOrDefault(x =>
                string.Equals(x.Servidor, record.Servidor, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.BaseDatos, record.BaseDatos, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Usuario, record.UsuarioSql, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
            throw new InvalidOperationException("La base solicitada por el ticket no está registrada en ALFA_CENTRAL.");

        sessionService.SwitchSession(existing.Id);
        return existing;
    }

    private async Task<AppUserSessionInfo> LoginAppUserAsync(Vb6BridgeTicketRecord record, CancellationToken ct)
    {
        var session = sessionService.GetActiveSession()
            ?? throw new InvalidOperationException("No hay una sesión SQL activa.");

        if (!string.Equals(session.Servidor, record.Servidor, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.BaseDatos, record.BaseDatos, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.Usuario, record.UsuarioSql, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La sesión SQL activa no coincide con la solicitada por el ticket.");
        }

        return await legacyBaseUserSession.LoginAsync(record.UsuarioSistema, record.PasswordSistema, ct);
    }

    private static async Task ValidateSqlConnectionAsync(SqlSessionData sqlSession, CancellationToken ct)
    {
        await using var cn = new SqlConnection(sqlSession.ConnectionString);
        await cn.OpenAsync(ct);
    }

    private async Task<AppUserSessionInfo> ValidateAppUserAsync(string connectionString, string userName, string password, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString);
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
            throw new InvalidOperationException("El usuario del sistema no existe en la base activa.");

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

        return new AppUserSessionInfo
        {
            UserName = canonicalUser,
            Email = email,
            SystemCode = SistemaFijo
        };
    }

    private static SqlSessionData BuildSqlSession(Vb6AuthTicketRequest request)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = request.Servidor.Trim(),
            InitialCatalog = request.BaseDatos.Trim(),
            UserID = request.UsuarioSql.Trim(),
            Password = request.PasswordSql,
            TrustServerCertificate = true,
            ApplicationName = "AlfaCore"
        };

        return new SqlSessionData(request.Servidor.Trim(), request.BaseDatos.Trim(), request.UsuarioSql.Trim(), request.PasswordSql, builder.ConnectionString);
    }

    private static string NormalizeModule(string? module)
    {
        var value = (module ?? string.Empty).Trim().Trim('/');
        return value.ToLowerInvariant();
    }

    private static bool GetBool(SqlDataReader reader, int ordinal)
        => !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);

    private static string GetString(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static async Task<bool> HasActivoColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_USUARIOS')
              AND LOWER(name) = N'activo';
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        var count = scalar is null ? 0 : Convert.ToInt32(scalar);
        return count > 0;
    }

    private sealed record SqlSessionData(
        string Servidor,
        string BaseDatos,
        string Usuario,
        string Password,
        string ConnectionString);
}
