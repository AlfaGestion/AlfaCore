using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class Vb6BridgeService(
    IConfiguration configuration,
    ISessionService sessionService,
    ILegacyBaseUserSessionService legacyBaseUserSession,
    UsuariosPasswordCodec passwordCodec,
    Vb6BridgeTicketStore ticketStore,
    IAppModeService appMode,
    ICentralBasesService centralBasesService,
    ICentralClientesService centralClientesService,
    AppUserSessionStore appUserSessionStore) : IVb6BridgeService
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

        string? idCliente = null;
        string? idWeb = null;
        string? razonSocial = null;
        var superAdmin = false;
        var idBase = 0;

        if (appMode.IsSaaSMode)
        {
            // No se puede resolver el cliente comparando sqlSession.Servidor contra bases.dbserver:
            // el VB6 conoce el servidor SQL por su nombre/IP en la LAN del cliente, mientras que en
            // ALFA_CENTRAL suele estar la IP de WireGuard con la que el servidor SaaS llega a esa
            // misma base — y dbname/dbuser solos pueden repetirse entre clientes con instalaciones
            // legacy que comparten nombres genéricos. Por eso el VB6 tiene que decir explícitamente
            // qué fila de bases es (Cfg("ALFACORE_IDBASE"), configurado una vez por equipo).
            if (!int.TryParse(request.IdBaseCentral, out idBase) || idBase <= 0)
            {
                throw new Vb6IdBaseCentralRequeridoException(
                    "Falta configurar Cfg(\"ALFACORE_IDBASE\") en este equipo (id de la base en ALFA_CENTRAL). " +
                    "AlfaCore SaaS no puede inferir el cliente a partir del servidor SQL local.");
            }

            var baseCentral = await centralBasesService.GetByIdAsync(idBase, ct)
                ?? throw new InvalidOperationException($"No existe la base #{idBase} en ALFA_CENTRAL (revisar Cfg(\"ALFACORE_IDBASE\")).");

            // dbserver NO se compara: el VB6 conoce el servidor SQL por su nombre/IP en la LAN
            // del cliente, mientras que acá suele estar la IP de WireGuard. dbname/dbuser/dbpassword
            // sí tienen que coincidir con lo que mandó el VB6 — las tres juntas son una forma barata
            // de detectar un ALFACORE_IDBASE mal cargado (ej. copiado de otro cliente), incluso en el
            // caso límite de dos clientes con el mismo nombre de base/usuario en instalaciones legacy.
            if (!string.Equals(baseCentral.DbName.Trim(), sqlSession.BaseDatos, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(baseCentral.DbUser.Trim(), sqlSession.Usuario, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(baseCentral.DbPassword.Trim(), sqlSession.Password.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cfg(\"ALFACORE_IDBASE\")={idBase} no corresponde a la base local " +
                    $"({sqlSession.BaseDatos} / {sqlSession.Usuario}). Revisar la configuración de este equipo " +
                    "o consultar al administrador del sistema.");
            }

            var cliente = await centralClientesService.GetByIdClienteAsync(baseCentral.IdCliente, ct);
            if (cliente is null || string.IsNullOrWhiteSpace(cliente.IdWeb))
                throw new InvalidOperationException($"El cliente '{baseCentral.IdCliente}' no tiene IdWeb configurado en ALFA_CENTRAL.");

            idCliente = cliente.IdCliente;
            idWeb = cliente.IdWeb;
            razonSocial = cliente.RazonSocial;
            superAdmin = cliente.SuperAdmin;
        }

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
                : request.NombreSesion.Trim(),
            IdCliente = idCliente,
            IdWeb = idWeb,
            RazonSocial = razonSocial,
            SuperAdmin = superAdmin,
            IdBase = idBase
        });
    }

    public async Task<Vb6ConsumeTicketResult> ConsumeTicketAsync(string ticket, CancellationToken ct = default)
    {
        if (!ticketStore.TryConsume(ticket, out var record) || record is null)
            throw new InvalidOperationException("El ticket no existe o ya expiró.");

        if (!string.IsNullOrWhiteSpace(record.IdCliente))
            return await ConsumeSaaSTicketAsync(record, ct);

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

    /// <summary>
    /// Ticket de AlfaCore SaaS: no depende de <see cref="ISessionService"/> (que en modo SaaS solo
    /// conoce las bases del usuario central ya logueado). En su lugar arma directamente la identidad
    /// central (IdCliente/IdWeb resueltos en <see cref="CreateTicketAsync"/> contra ALFA_CENTRAL) y la
    /// publica en <see cref="AppUserSessionStore"/> para que el próximo circuito Blazor la recupere via
    /// el token guardado en localStorage, igual que hace un login normal.
    /// </summary>
    private async Task<Vb6ConsumeTicketResult> ConsumeSaaSTicketAsync(Vb6BridgeTicketRecord record, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = record.Servidor,
            InitialCatalog = record.BaseDatos,
            UserID = record.UsuarioSql,
            Password = record.PasswordSql,
            TrustServerCertificate = true,
            ApplicationName = "AlfaCore"
        };

        var user = await ValidateAppUserAsync(builder.ConnectionString, record.UsuarioSistema, record.PasswordSistema, ct);

        var centralUser = new AppUserSessionInfo
        {
            UserName = user.UserName,
            Email = user.Email,
            CentralLogin = user.UserName,
            SystemCode = user.SystemCode,
            LoginAt = DateTime.Now,
            IdCliente = record.IdCliente ?? string.Empty,
            RazonSocial = record.RazonSocial ?? string.Empty,
            IdWeb = record.IdWeb ?? string.Empty,
            SuperAdmin = record.SuperAdmin,
            RequiresInternalLogin = false
        };

        var token = appUserSessionStore.Store(centralUser);

        return new Vb6ConsumeTicketResult
        {
            SqlSessionId = record.IdBase.ToString(),
            UserToken = token,
            RedirectUrl = $"/{Uri.EscapeDataString(record.IdWeb ?? string.Empty)}/{record.IdBase}/{record.Modulo}?directo=1"
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
