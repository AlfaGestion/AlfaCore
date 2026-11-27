using System.Net;
using System.Net.Mail;
using AlfaCore.Compartido;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Administracion central de las consultas con IA (servidor MCP, proyecto AlfaCore.Mcp) desde /admin:
/// habilitar una base (crea su login de SOLO LECTURA mcp_ro_&lt;idbase&gt; con el usuario SQL registrado para la base y guarda
/// la clave cifrada en ALFA_CENTRAL.MCP_BASE) y decidir que usuarios (mails de dbo.users) consultan que bases (MCP_ACCESO).
/// El MCP lee estas mismas tablas: no hay que reiniciar nada (lo toma en menos de 30 s).
/// </summary>
public interface ICentralMcpService
{
    Task<McpEstadoClienteDto> GetEstadoClienteAsync(string idCliente, CancellationToken ct = default);
    Task<McpHabilitacionResultadoDto> HabilitarBaseAsync(int idBase, CancellationToken ct = default);
    Task DeshabilitarBaseAsync(int idBase, CancellationToken ct = default);
    Task<McpAccesosGuardadosDto> GuardarAccesosAsync(string usuario, string idCliente, IReadOnlyCollection<int> idBases, bool invitar, CancellationToken ct = default);
}

public sealed class CentralMcpService(IConfiguration configuration, IAppUserSessionService user, IAppEventService events) : ICentralMcpService
{
    private const string Modulo = "ConsultasIaMcp";
    private const string UrlMcpPorDefecto = "https://alfacore-mcp.ddns.net/mcp";

    private string CentralCs => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No está configurada la conexión central.");

    private string UrlMcp => string.IsNullOrWhiteSpace(configuration["Mcp:UrlPublica"]) ? UrlMcpPorDefecto : configuration["Mcp:UrlPublica"]!.Trim();

    // ---- estado ------------------------------------------------------------------------------------------------------
    public Task<McpEstadoClienteDto> GetEstadoClienteAsync(string idCliente, CancellationToken ct = default)
        => LoggedAsync("Estado", async () =>
        {
            EnsureAdmin();
            await using var cn = new SqlConnection(CentralCs);
            if (!await EsquemaListoAsync(cn, ct)) return new McpEstadoClienteDto(false, new Dictionary<int, McpBaseEstadoDto>(), []);
            var p = new { IdCliente = (idCliente ?? string.Empty).Trim() };
            var bases = await cn.QueryAsync<McpBaseEstadoDto>(new CommandDefinition("""
                SELECT mb.IdBase, mb.Habilitada, mb.UsuarioSql, mb.Modificado
                FROM dbo.MCP_BASE mb JOIN dbo.bases b ON b.id = mb.IdBase
                WHERE LTRIM(RTRIM(b.idcliente)) = @IdCliente;
                """, p, cancellationToken: ct));
            var accesos = await cn.QueryAsync<McpAccesoDto>(new CommandDefinition("""
                SELECT a.Email, a.IdBase, a.Activo, a.Invitado AS InvitadoUtc, a.UltimoIngreso AS UltimoIngresoUtc
                FROM dbo.MCP_ACCESO a JOIN dbo.bases b ON b.id = a.IdBase
                WHERE LTRIM(RTRIM(b.idcliente)) = @IdCliente;
                """, p, cancellationToken: ct));
            return new McpEstadoClienteDto(true, bases.ToDictionary(b => b.IdBase), accesos.ToList());
        }, ct);

    // ---- habilitar / deshabilitar una base -------------------------------------------------------------------------
    public Task<McpHabilitacionResultadoDto> HabilitarBaseAsync(int idBase, CancellationToken ct = default)
        => LoggedAsync("HabilitarBase", async () =>
        {
            EnsureAdmin();
            await using var central = new SqlConnection(CentralCs);
            await RequireEsquemaAsync(central, ct);
            var b = await central.QuerySingleOrDefaultAsync<BaseRow>(new CommandDefinition("""
                SELECT id AS IdBase, LTRIM(RTRIM(idcliente)) AS IdCliente, ISNULL(nombre, '') AS Nombre, ISNULL(dbserver, '') AS DbServer,
                       ISNULL(dbname, '') AS DbName, ISNULL(dbuser, '') AS DbUser, ISNULL(dbpassword, '') AS DbPassword
                FROM dbo.bases WHERE id = @idBase;
                """, new { idBase }, cancellationToken: ct)) ?? throw new InvalidOperationException("La base ya no existe. Actualizá el listado.");
            if (b.DbServer.Length == 0 || b.DbName.Length == 0 || b.DbUser.Length == 0)
                throw new InvalidOperationException("La base no tiene servidor, nombre o usuario SQL registrados: completalos con Editar.");

            var login = ClaveBaseMcp.NombreLogin(idBase);
            var clave = ClaveBaseMcp.NuevaClave();

            // 1) Con el usuario SQL registrado para la base: esquema listo, login propio y permisos (los mismos del script 005).
            await using (var cn = new SqlConnection(Cs(b.DbServer, b.DbName, b.DbUser, b.DbPassword)))
            {
                try { await cn.OpenAsync(ct); }
                catch (SqlException ex) { throw new InvalidOperationException($"No se pudo conectar a la base con el usuario registrado ({b.DbUser}): {ex.Message}"); }

                var listo = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT CASE WHEN OBJECT_ID(N'dir.vw_Config', N'V') IS NOT NULL AND OBJECT_ID(N'dbo.IA_LOG_CONSULTAS', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
                    cancellationToken: ct));
                if (listo != 1)
                    throw new InvalidOperationException("Esta base todavía no tiene el esquema de consultas con IA. Entrá a esa base con AlfaCore para que se aplique " +
                                                        "la actualización 2026-11-27-040__ia_mcp_esquema_dir.sql y volvé a intentar.");
                try
                {
                    await cn.ExecuteAsync(new CommandDefinition(SqlCrearLogin, new { Login = login, Clave = clave }, cancellationToken: ct));
                }
                catch (SqlException ex) when (ex.Number is 15247 or 15151 or 229 or 262 or 300 or 916)
                {
                    throw new InvalidOperationException($"El usuario SQL registrado para esta base ({b.DbUser}) no tiene permiso para crear logins en ese servidor. " +
                                                        "Opciones: registrar la base con un usuario administrador, o crear el login a mano con " +
                                                        "dir-mcp/005_login_solo_lectura.sql y habilitarla con usuarios_mcp.ps1 habilitar-base.");
                }
            }

            // 2) Con el login nuevo: tiene que leer el esquema dir y NADA de las tablas de la base.
            string? vistasConError;
            await using (var ro = new SqlConnection(Cs(b.DbServer, b.DbName, login, clave)))
            {
                try { await ro.OpenAsync(ct); }
                catch (SqlException ex) when (ex.Number == 18456)
                {
                    throw new InvalidOperationException("El login se creó pero el servidor no lo deja entrar. Revisá que ese SQL Server acepte " +
                                                        "autenticación de SQL Server (modo mixto).");
                }
                var r = await ro.QuerySingleAsync<(int Claves, int? LeeTablas, string? Errores)>(new CommandDefinition(
                    "SELECT (SELECT COUNT(*) FROM dir.vw_Config), HAS_PERMS_BY_NAME('dbo.TA_CONFIGURACION', 'OBJECT', 'SELECT'), " +
                    "(SELECT MAX(Valor) FROM dir.vw_Config WHERE Clave = 'IA_MCP_ESQUEMA_ERRORES');", cancellationToken: ct));
                if (r.LeeTablas == 1)
                    throw new InvalidOperationException($"El login {login} puede leer tablas de la base (¿está en algún rol como db_datareader?). " +
                                                        "Por seguridad la base no se habilitó: sacalo de ese rol y volvé a intentar.");
                vistasConError = string.IsNullOrWhiteSpace(r.Errores) ? null : r.Errores.Trim();
            }

            // 3) La clave queda solo cifrada (DPAPI de este servidor). El MCP la toma en menos de 30 s.
            await central.ExecuteAsync(new CommandDefinition("""
                IF EXISTS (SELECT 1 FROM dbo.MCP_BASE WHERE IdBase = @idBase)
                    UPDATE dbo.MCP_BASE SET UsuarioSql = @login, ClaveCifrada = @cifrada, Habilitada = 1, Modificado = GETUTCDATE() WHERE IdBase = @idBase;
                ELSE
                    INSERT INTO dbo.MCP_BASE (IdBase, UsuarioSql, ClaveCifrada) VALUES (@idBase, @login, @cifrada);
                """, new { idBase, login, cifrada = ClaveBaseMcp.Cifrar(clave) }, cancellationToken: ct));

            await events.LogAuditAsync("Central", "McpHabilitarBase", "bases", idBase.ToString(),
                "Base habilitada para consultas con IA (login de solo lectura creado o renovado).",
                new { b.IdCliente, b.Nombre, Login = login, VistasConError = vistasConError }, ct);
            return new McpHabilitacionResultadoDto(login, vistasConError);
        }, ct);

    /// <summary>
    /// T-SQL que corre con el usuario registrado para la base. Nombre y clave van como PARAMETROS y se escapan con QUOTENAME
    /// (la clave ademas solo usa caracteres sin comillas ni corchetes). Si el login ya existe se le cambia la clave y se reactiva:
    /// es propio de esta base (mcp_ro_&lt;idbase&gt;), no lo usa nadie mas.
    /// </summary>
    internal const string SqlCrearLogin = """
        DECLARE @sql nvarchar(max);
        IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @Login)
            SET @sql = N'CREATE LOGIN ' + QUOTENAME(@Login) + N' WITH PASSWORD = ' + QUOTENAME(@Clave, '''') +
                       N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME());
        ELSE
            SET @sql = N'ALTER LOGIN ' + QUOTENAME(@Login) + N' WITH PASSWORD = ' + QUOTENAME(@Clave, '''') +
                       N'; ALTER LOGIN ' + QUOTENAME(@Login) + N' ENABLE';
        EXEC (@sql);
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @Login)
            SET @sql = N'CREATE USER ' + QUOTENAME(@Login) + N' FOR LOGIN ' + QUOTENAME(@Login) + N' WITH DEFAULT_SCHEMA = [dir]';
        ELSE
            SET @sql = N'ALTER USER ' + QUOTENAME(@Login) + N' WITH LOGIN = ' + QUOTENAME(@Login);
        EXEC (@sql);
        SET @sql = N'GRANT SELECT ON SCHEMA::[dir] TO ' + QUOTENAME(@Login) + N'; GRANT INSERT ON OBJECT::[dbo].[IA_LOG_CONSULTAS] TO ' + QUOTENAME(@Login);
        EXEC (@sql);
        """;

    public Task DeshabilitarBaseAsync(int idBase, CancellationToken ct = default)
        => LoggedAsync("DeshabilitarBase", async () =>
        {
            EnsureAdmin();
            await using var cn = new SqlConnection(CentralCs);
            await RequireEsquemaAsync(cn, ct);
            // El MCP deja de usarla en menos de 30 s; ademas se cortan las sesiones abiertas contra esa base.
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.MCP_BASE SET Habilitada = 0, Modificado = GETUTCDATE() WHERE IdBase = @idBase;
                UPDATE dbo.MCP_REFRESH_TOKEN SET Revocado = 1 WHERE Base = @baseTexto AND Revocado = 0;
                """, new { idBase, baseTexto = idBase.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken: ct));
            await events.LogAuditAsync("Central", "McpDeshabilitarBase", "bases", idBase.ToString(), "Base deshabilitada para consultas con IA.", null, ct);
            return true;
        }, ct);

    // ---- accesos por usuario ----------------------------------------------------------------------------------------
    public Task<McpAccesosGuardadosDto> GuardarAccesosAsync(string usuario, string idCliente, IReadOnlyCollection<int> idBases, bool invitar, CancellationToken ct = default)
        => LoggedAsync("GuardarAccesos", async () =>
        {
            EnsureAdmin();
            var email = NormalizarEmail(usuario)
                ?? throw new InvalidOperationException("Este usuario no es un mail. Para consultar con IA el ingreso es con un código que se envía por mail: " +
                                                       "creá el usuario con su mail.");
            var cliente = (idCliente ?? string.Empty).Trim();
            await using var cn = new SqlConnection(CentralCs);
            await RequireEsquemaAsync(cn, ct);
            await cn.OpenAsync(ct);

            var esDelCliente = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.users WHERE LOWER(LTRIM(RTRIM([user]))) = @email AND LTRIM(RTRIM(idcliente)) = @cliente;",
                new { email, cliente }, cancellationToken: ct));
            if (esDelCliente == 0) throw new InvalidOperationException("El usuario ya no pertenece a este cliente. Actualizá el listado.");

            // Bases del cliente: cuales estan habilitadas y como se llaman.
            var basesCliente = (await cn.QueryAsync<(int IdBase, string Nombre, bool Habilitada)>(new CommandDefinition("""
                SELECT b.id, ISNULL(b.nombre, ''), CAST(CASE WHEN mb.Habilitada = 1 THEN 1 ELSE 0 END AS bit)
                FROM dbo.bases b LEFT JOIN dbo.MCP_BASE mb ON mb.IdBase = b.id
                WHERE LTRIM(RTRIM(b.idcliente)) = @cliente;
                """, new { cliente }, cancellationToken: ct))).ToDictionary(x => x.IdBase);
            var elegidas = idBases.Distinct().ToList();
            foreach (var id in elegidas)
            {
                if (!basesCliente.TryGetValue(id, out var bc)) throw new InvalidOperationException("Una de las bases elegidas no es de este cliente. Actualizá el listado.");
                if (!bc.Habilitada) throw new InvalidOperationException($"La base {bc.Nombre} no está habilitada para consultas con IA: habilitala primero en Bases registradas.");
            }

            var activas = (await cn.QueryAsync<int>(new CommandDefinition(
                "SELECT IdBase FROM dbo.MCP_ACCESO WHERE Email = @email AND Activo = 1;", new { email }, cancellationToken: ct)))
                .Where(basesCliente.ContainsKey).ToHashSet();
            var agregar = elegidas.Where(id => !activas.Contains(id)).ToList();
            var quitar = activas.Where(id => !elegidas.Contains(id)).ToList();

            await using (var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct))
            {
                foreach (var id in agregar)
                    await cn.ExecuteAsync(new CommandDefinition("""
                        IF EXISTS (SELECT 1 FROM dbo.MCP_ACCESO WHERE Email = @email AND IdBase = @id)
                            UPDATE dbo.MCP_ACCESO SET Activo = 1 WHERE Email = @email AND IdBase = @id;
                        ELSE
                            INSERT INTO dbo.MCP_ACCESO (Email, IdBase) VALUES (@email, @id);
                        """, new { email, id }, tx, cancellationToken: ct));
                foreach (var id in quitar)
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.MCP_ACCESO SET Activo = 0 WHERE Email = @email AND IdBase = @id;
                        UPDATE dbo.MCP_REFRESH_TOKEN SET Revocado = 1
                        WHERE Base = @baseTexto AND Revocado = 0 AND UsuarioId IN (SELECT Id FROM dbo.MCP_USUARIO WHERE Usuario = @email);
                        """, new { email, id, baseTexto = id.ToString(System.Globalization.CultureInfo.InvariantCulture) }, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
            }

            var enviada = false;
            if (invitar && elegidas.Count > 0)
            {
                await EnviarInvitacionAsync(email, elegidas.Select(id => basesCliente[id].Nombre).ToList(), ct);
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.MCP_ACCESO SET Invitado = GETUTCDATE() WHERE Email = @email AND IdBase IN @ids;", new { email, ids = elegidas }, cancellationToken: ct));
                enviada = true;
            }

            await events.LogAuditAsync("Central", "McpGuardarAccesos", "users", email,
                "Accesos a consultas con IA actualizados.", new { IdCliente = cliente, Bases = elegidas, Agregadas = agregar, Quitadas = quitar, Invitacion = enviada }, ct);
            return new McpAccesosGuardadosDto(agregar.Count, quitar.Count, enviada);
        }, ct);

    private async Task EnviarInvitacionAsync(string email, IReadOnlyList<string> bases, CancellationToken ct)
    {
        string Cfg(string k) => (configuration[k] ?? string.Empty).Trim();
        var server = Cfg("RegistroPublico:EmailServer");
        var cuenta = Cfg("RegistroPublico:EmailAccount");
        if (server.Length == 0 || cuenta.Length == 0 || !int.TryParse(Cfg("RegistroPublico:EmailPort"), out var puerto) || puerto <= 0)
            throw new InvalidOperationException("Los accesos se guardaron, pero la invitación no se envió: falta configurar el correo del servidor (RegistroPublico:Email*).");
        var ssl = Cfg("RegistroPublico:EmailSsl").ToUpperInvariant() is "SI" or "TRUE" or "1";

        using var msg = new MailMessage(new MailAddress(cuenta, "AlfaCore"), new MailAddress(email))
        {
            Subject = "Tu acceso a AlfaCore desde Claude o ChatGPT",
            Body = TextoInvitacion(email, bases, UrlMcp),
            IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        using var smtp = new SmtpClient(server, puerto)
        {
            EnableSsl = ssl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(cuenta, Cfg("RegistroPublico:EmailPassword")),
            Timeout = 20000
        };
        try { await smtp.SendMailAsync(msg, ct); }
        catch (SmtpException ex) { throw new InvalidOperationException($"Los accesos se guardaron, pero la invitación no se pudo enviar: {ex.Message}"); }
    }

    internal static string TextoInvitacion(string email, IReadOnlyList<string> bases, string urlMcp)
    {
        var lista = bases.Count == 1 ? $"\"{bases[0]}\"" : string.Join(", ", bases.Select(b => $"\"{b}\""));
        return $"""
            Hola:

            Ya podés consultar los datos de {lista} (ventas, compras, clientes, proveedores, stock y cuenta corriente)
            conversando con Claude o ChatGPT. El acceso es solo de lectura: puede consultar, nunca modificar.

            Cómo conectarlo (una sola vez):

            CLAUDE (claude.ai o la app):
              1. Configuración > Conectores > Agregar conector personalizado.
              2. Nombre: AlfaCore.  URL: {urlMcp}
              3. Tocá Conectar. Se abre nuestra página: escribí este mail ({email}).
              4. Te llega un código de 6 dígitos a este mail: escribilo y listo.{(bases.Count > 1 ? "\n     Como tenés varias bases, elegís cuál consultar (para cambiar, desconectá y volvé a conectar)." : "")}

            CHATGPT:
              1. En Configuración, sección de conectores / aplicaciones, activar el modo desarrollador.
              2. Crear un conector. Nombre: AlfaCore.  URL: {urlMcp}  Autenticación: OAuth.
              3. Seguí los mismos pasos 3 y 4.

            Después preguntá, por ejemplo: "Usando AlfaCore, ¿cuánto vendí este mes por rubro?"

            Cada vez que se conecte una aplicación nueva te avisamos por mail.

            ALFA NET - Soluciones Informáticas
            """;
    }

    // ---- utilidades -----------------------------------------------------------------------------------------------
    internal static string? NormalizarEmail(string? valor)
    {
        var s = (valor ?? string.Empty).Trim().ToLowerInvariant();
        var at = s.IndexOf('@');
        return s.Length is >= 6 and <= 200 && at > 0 && at == s.LastIndexOf('@') && s[(at + 1)..].Contains('.') && !s.Any(char.IsWhiteSpace) ? s : null;
    }

    private static string Cs(string servidor, string db, string usuario, string clave) => new SqlConnectionStringBuilder
    {
        DataSource = servidor, InitialCatalog = db, UserID = usuario, Password = clave,
        TrustServerCertificate = true, Encrypt = false, ConnectTimeout = 15, ApplicationName = "AlfaCore.Admin.Mcp", Pooling = false
    }.ConnectionString;

    private void EnsureAdmin()
    {
        if (user.CurrentUser?.SuperAdmin != true)
            throw new InvalidOperationException("Solo un administrador central puede administrar las consultas con IA.");
    }

    private static Task<bool> EsquemaListoAsync(SqlConnection cn, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CAST(CASE WHEN OBJECT_ID('dbo.MCP_BASE','U') IS NOT NULL AND OBJECT_ID('dbo.MCP_ACCESO','U') IS NOT NULL
                AND COL_LENGTH('dbo.MCP_REFRESH_TOKEN','Base') IS NOT NULL THEN 1 ELSE 0 END AS bit);
            """, cancellationToken: ct));

    private static async Task RequireEsquemaAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await EsquemaListoAsync(cn, ct))
            throw new InvalidOperationException("Falta aplicar en ALFA_CENTRAL el script 2026-11-27-042__alfa_central_mcp_multibase.sql.");
    }

    private async Task<T> LoggedAsync<T>(string action, Func<Task<T>> operation, CancellationToken ct)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var message = ex is InvalidOperationException or ArgumentException ? ex.Message : "No se pudo completar la operación de consultas con IA.";
            var incident = await events.LogErrorAsync(Modulo, action, ex, message, ct: ct);
            throw new AppUserFacingException(message, incident, ex);
        }
    }

    private sealed class BaseRow
    {
        public int IdBase { get; init; }
        public string IdCliente { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
        public string DbServer { get; init; } = string.Empty;
        public string DbName { get; init; } = string.Empty;
        public string DbUser { get; init; } = string.Empty;
        public string DbPassword { get; init; } = string.Empty;
    }
}
