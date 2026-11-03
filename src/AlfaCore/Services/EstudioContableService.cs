using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// SQL compartido del portal del estudio contable sobre ALFA_CENTRAL (ESTUDIO_ACCESOS) y sobre la base de la empresa.
/// El acceso del estudio a una base es un usuario común de TA_USUARIOS que crea el dueño: el estudio entra porque ese
/// usuario tiene como email el login central del estudio (el mismo ingreso automático que ya usa AlfaCore por email).
/// Revocar = desactivar ese usuario y marcar el acceso como REVOCADO.
/// </summary>
internal static class EstudioContableCentral
{
    internal const string Pendiente = "PENDIENTE", Activo = "ACTIVO", Revocado = "REVOCADO";
    internal const string TipoClienteEstudio = "E";
    internal static readonly TimeSpan VigenciaInvitacion = TimeSpan.FromDays(14);
    private static volatile bool _esquemaListo;

    // Va antes de Perfiles: los inicializadores estáticos corren en orden.
    private static readonly string[] Consulta =
    [
        "D95", "D9502", "D957001", "D957002", "D957003", "D957004", "D957005", "D957006", "D016021", "D9832",
        "D60", "D6000", "D6009", "D60RPTWEB", "D50", "D5001", "D5004", "D5005", "D75", "D7510", "D8079", "DTABLEROS"
    ];

    /// <summary>Menús de cada perfil. Después el dueño los amplía o recorta como a cualquier usuario (Autorización de tareas).</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> Perfiles = new Dictionary<string, string[]>
    {
        // Consulta: contabilidad, libros de IVA, retenciones, paquete, reportes y tableros (ventas, compras, caja, stock).
        ["CONSULTA"] = [.. Consulta],
        // Completo (por defecto): además carga de asientos (con modificar y anular), control e importación ARCA, fechas de cierre,
        // recepción documental y conciliaciones.
        ["COMPLETO"] = [.. Consulta, "D9550", "D016023", "D9882", "D016004", "D75-CONCILIA-IA", "D75-TARJETAS-IA"]
    };


    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    internal static string NuevoToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    internal static async Task<SqlConnection> AbrirAsync(IConfiguration configuration, CancellationToken ct, bool asegurarEsquema = true)
    {
        var cs = configuration.GetConnectionString("AlfaCentral");
        if (string.IsNullOrWhiteSpace(cs)) throw new InvalidOperationException("Falta configurar la conexión central (ConnectionStrings:AlfaCentral).");
        var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        if (asegurarEsquema && !_esquemaListo)
        {
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(CentralEstudiosSchema.VerificacionSql, cancellationToken: ct)) == 0)
                await cn.ExecuteAsync(new CommandDefinition(CentralEstudiosSchema.Script, commandTimeout: 120, cancellationToken: ct));
            _esquemaListo = true;
        }
        return cn;
    }

    internal static string ConexionBase(BaseCentralDto b) => new SqlConnectionStringBuilder
    {
        DataSource = b.DbServer, InitialCatalog = b.DbName, UserID = b.DbUser, Password = b.DbPassword.Trim(),
        TrustServerCertificate = true, ApplicationName = "AlfaCore", ConnectTimeout = 15
    }.ConnectionString;

    internal const string SelectAcceso = """
        SELECT a.IdAcceso, a.IdBase, a.EmailInvitado, a.UsuarioBase, a.Perfil, a.Estado, ISNULL(a.IdClienteEstudio,'') IdClienteEstudio,
               ISNULL(c.nombre,'') NombreEstudio, ISNULL(a.LoginEstudio,'') LoginEstudio, ISNULL(a.InvitadoPor,'') InvitadoPor,
               a.FechaInvitacion, a.FechaAceptacion, a.TokenVence, a.FechaRevocacion
        FROM dbo.ESTUDIO_ACCESOS a LEFT JOIN dbo.Clientes c ON c.idcliente = a.IdClienteEstudio
        """;

    /// <summary>Bases con acceso ACTIVO para un cliente de tipo estudio.</summary>
    internal static async Task<IReadOnlyList<int>> BasesConcedidasAsync(SqlConnection cn, string idCliente, CancellationToken ct)
        => (await cn.QueryAsync<int>(new CommandDefinition(
            "SELECT DISTINCT IdBase FROM dbo.ESTUDIO_ACCESOS WHERE IdClienteEstudio = @IdCliente AND Estado = 'ACTIVO';",
            new { IdCliente = idCliente }, cancellationToken: ct))).ToList();

    /// <summary>Usuario de la base para el estudio: activo y con el email que hace el ingreso automático (o inactivo y sin email).</summary>
    internal static Task SincronizarUsuarioBaseAsync(SqlConnection baseCn, string usuario, string email, bool activo, CancellationToken ct)
        => baseCn.ExecuteAsync(new CommandDefinition($"""
            UPDATE dbo.TA_USUARIOS SET email_de = @Email, FechaHora_Modificacion = GETDATE()
                {(activo ? "" : ", PASSWORD = @Password")}
            WHERE NOMBRE = @Usuario AND ISNULL(EsGrupo,0) = 0;
            IF COL_LENGTH(N'dbo.TA_USUARIOS', N'Activo') IS NOT NULL
                EXEC sys.sp_executesql N'UPDATE dbo.TA_USUARIOS SET Activo = @Activo WHERE NOMBRE = @Usuario AND ISNULL(EsGrupo,0) = 0;',
                    N'@Activo bit, @Usuario nvarchar(50)', @Activo = @Activo, @Usuario = @Usuario;
            """, new { Usuario = usuario, Email = email, Activo = activo, Password = new UsuariosPasswordCodec().Encode(NuevoToken()[..12]) }, cancellationToken: ct));
}

public interface IEstudioAccesosService
{
    Task<bool> PuedeAdministrarAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EstudioAcceso>> ListarAsync(CancellationToken ct = default);
    Task<EstudioInvitacionCreada> InvitarAsync(EstudioInvitarSolicitud solicitud, string baseUri, CancellationToken ct = default);
    Task<EstudioInvitacionCreada> RenovarEnlaceAsync(int idAcceso, string baseUri, CancellationToken ct = default);
    Task RevocarAsync(int idAcceso, CancellationToken ct = default);
}

/// <summary>Del lado de la empresa: el dueño (quien administra usuarios) invita y revoca estudios para la base activa.</summary>
public sealed class EstudioAccesosService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions,
    IAppEventService events, IConfiguration configuration) : IEstudioAccesosService
{
    internal static readonly string[] MenusAdministrar = ["D015001", "D015002"];
    private const string SistemaFijo = "CN000PR";
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public async Task<bool> PuedeAdministrarAsync(CancellationToken ct = default)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _)) return false;
        foreach (var m in MenusAdministrar) if (await permissions.HasAccessAsync(m, ct)) return true;
        return false;
    }

    private int IdBase() => sessions.GetActiveSession()?.BaseId is int id and > 0 ? id
        : throw new ReglaException("El acceso de estudios se administra desde AlfaCore en la nube (base SaaS).");

    public Task<IReadOnlyList<EstudioAcceso>> ListarAsync(CancellationToken ct = default) => EjecutarAsync("Listar", async () =>
    {
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        return (IReadOnlyList<EstudioAcceso>)(await central.QueryAsync<EstudioAcceso>(new CommandDefinition(
            EstudioContableCentral.SelectAcceso + " WHERE a.IdBase = @IdBase ORDER BY CASE a.Estado WHEN 'ACTIVO' THEN 0 WHEN 'PENDIENTE' THEN 1 ELSE 2 END, a.FechaInvitacion DESC;",
            new { IdBase = IdBase() }, cancellationToken: ct))).ToList();
    }, ct);

    internal static string UsuarioDesdeEmail(string email)
    {
        var local = email.Split('@')[0];
        var limpio = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        var nombre = "EST_" + (limpio.Length == 0 ? "CONTADOR" : limpio);
        return nombre.Length > 30 ? nombre[..30] : nombre;
    }

    internal static string NormalizarEmail(string email)
    {
        var e = (email ?? "").Trim();
        if (e.Length is < 5 or > 150 || e.Count(c => c == '@') != 1 || e.StartsWith('@') || !e[(e.IndexOf('@') + 1)..].Contains('.') || e.Any(char.IsWhiteSpace))
            throw new ReglaException("Ingresá un email válido del contador.");
        return e.ToLowerInvariant();
    }

    public Task<EstudioInvitacionCreada> InvitarAsync(EstudioInvitarSolicitud s, string baseUri, CancellationToken ct = default) => EjecutarAsync("Invitar", async () =>
    {
        var email = NormalizarEmail(s.Email);
        var perfil = (s.Perfil ?? "").Trim().ToUpperInvariant();
        if (!EstudioContableCentral.Perfiles.TryGetValue(perfil, out var menus)) throw new ReglaException("Elegí el perfil del estudio.");
        var usuario = (s.Usuario ?? "").Trim();
        if (usuario.Length == 0) usuario = UsuarioDesdeEmail(email);
        if (usuario.Length > 50 || usuario.Any(c => c is '\'' or '"' or ';')) throw new ReglaException("El nombre de usuario no es válido.");
        var idBase = IdBase();
        var invitadoPor = users.GetCurrentUserName();

        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        if (await central.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.ESTUDIO_ACCESOS WHERE IdBase = @IdBase AND Estado <> 'REVOCADO' AND (EmailInvitado = @Email OR UsuarioBase = @Usuario);",
                new { IdBase = idBase, Email = email, Usuario = usuario }, cancellationToken: ct)) > 0)
            throw new ReglaException("Ese email o usuario ya tiene un acceso activo o pendiente en esta empresa.");

        // Usuario en la base: nuevo, o uno existente que no sea de otra persona (solo se reutiliza si ya era de un estudio).
        await using (var cn = new SqlConnection(sessions.GetConnectionString()))
        {
            await cn.OpenAsync(ct);
            var existente = await cn.QuerySingleOrDefaultAsync<(string Email, bool EsGrupo)?>(new CommandDefinition(
                "SELECT ISNULL(email_de,''), CONVERT(bit, ISNULL(EsGrupo,0)) FROM dbo.TA_USUARIOS WHERE NOMBRE = @Usuario;", new { Usuario = usuario }, cancellationToken: ct));
            if (existente is { } ex && (ex.EsGrupo || (!usuario.StartsWith("EST_", StringComparison.OrdinalIgnoreCase) && ex.Email.Length > 0)))
                throw new ReglaException($"Ya existe el usuario {usuario} en esta base. Elegí otro nombre.");
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var password = new UsuariosPasswordCodec().Encode(EstudioContableCentral.NuevoToken()[..12]);
            await cn.ExecuteAsync(new CommandDefinition("""
                IF NOT EXISTS (SELECT 1 FROM dbo.TA_USUARIOS WHERE NOMBRE = @Usuario)
                    INSERT INTO dbo.TA_USUARIOS (NOMBRE, SISTEMA, PASSWORD, FechaHora_Grabacion, CambiarProximoInicio, EsGrupo, email_de)
                    VALUES (@Usuario, @Sistema, @Password, GETDATE(), 0, 0, N'');
                ELSE
                    UPDATE dbo.TA_USUARIOS SET email_de = N'', PASSWORD = @Password, FechaHora_Modificacion = GETDATE() WHERE NOMBRE = @Usuario;
                """, new { Usuario = usuario, Sistema = SistemaFijo, Password = password }, tx, cancellationToken: ct));
            // Sin email hasta que el estudio acepta: nadie puede entrar todavía con ese usuario.
            // Perfil completo: puede modificar y anular asientos manuales (AstModificar/AstAnular del legacy).
            await cn.ExecuteAsync(new CommandDefinition($"""
                IF COL_LENGTH(N'dbo.TA_USUARIOS', N'Activo') IS NOT NULL
                    EXEC sys.sp_executesql N'UPDATE dbo.TA_USUARIOS SET Activo = 0 WHERE NOMBRE = @Usuario;', N'@Usuario nvarchar(50)', @Usuario = @Usuario;
                IF COL_LENGTH(N'dbo.TA_USUARIOS', N'AstModificar') IS NOT NULL AND COL_LENGTH(N'dbo.TA_USUARIOS', N'AstAnular') IS NOT NULL
                    EXEC sys.sp_executesql N'UPDATE dbo.TA_USUARIOS SET AstModificar = @Carga, AstAnular = @Carga WHERE NOMBRE = @Usuario;',
                        N'@Carga bit, @Usuario nvarchar(50)', @Carga = {(perfil == "COMPLETO" ? 1 : 0)}, @Usuario = @Usuario;
                IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
                BEGIN
                    DELETE FROM dbo.ALFACORE_TAREAS_WEB WHERE Usuario = @Usuario AND Sistema = @Sistema;
                    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
                    SELECT @Usuario, @Sistema, m.Clave, GETDATE(), @Por FROM {ValoresMenus(menus)};
                END
                """, new { Usuario = usuario, Sistema = SistemaFijo, Por = invitadoPor }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
        }

        var token = EstudioContableCentral.NuevoToken();
        var id = await central.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.ESTUDIO_ACCESOS (IdBase, EmailInvitado, UsuarioBase, Perfil, Estado, TokenHash, TokenVence, InvitadoPor)
            VALUES (@IdBase, @Email, @Usuario, @Perfil, 'PENDIENTE', @Hash, @Vence, @Por);
            SELECT CONVERT(int, SCOPE_IDENTITY());
            """, new { IdBase = idBase, Email = email, Usuario = usuario, Perfil = perfil, Hash = EstudioContableCentral.Hash(token),
                Vence = DateTime.Now + EstudioContableCentral.VigenciaInvitacion, Por = invitadoPor }, cancellationToken: ct));
        await events.LogAuditAsync("EstudioContable", "Invitar", "ESTUDIO_ACCESOS", id.ToString(CultureInfo.InvariantCulture),
            $"Invitó al estudio {email} (usuario {usuario}, perfil {perfil}).", new { idBase, email, usuario, perfil }, CancellationToken.None);
        var acceso = await central.QuerySingleAsync<EstudioAcceso>(new CommandDefinition(EstudioContableCentral.SelectAcceso + " WHERE a.IdAcceso = @Id;", new { Id = id }, cancellationToken: ct));
        return new EstudioInvitacionCreada(acceso, Enlace(baseUri, token));
    }, ct);

    /// <summary>Lista literal de menús (claves fijas del perfil, sin datos del usuario) para el INSERT … SELECT.</summary>
    internal static string ValoresMenus(IEnumerable<string> menus)
        => "(VALUES " + string.Join(",", menus.Where(m => m.Length > 0 && m.All(c => char.IsLetterOrDigit(c) || c == '-')).Select(m => $"(N'{m}')")) + ") m(Clave)";

    internal static string Enlace(string baseUri, string token) => $"{baseUri.TrimEnd('/')}/estudio/invitacion/{token}";

    public Task<EstudioInvitacionCreada> RenovarEnlaceAsync(int idAcceso, string baseUri, CancellationToken ct = default) => EjecutarAsync("RenovarEnlace", async () =>
    {
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        var token = EstudioContableCentral.NuevoToken();
        var filas = await central.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ESTUDIO_ACCESOS SET TokenHash = @Hash, TokenVence = @Vence WHERE IdAcceso = @Id AND IdBase = @IdBase AND Estado = 'PENDIENTE';
            """, new { Id = idAcceso, IdBase = IdBase(), Hash = EstudioContableCentral.Hash(token), Vence = DateTime.Now + EstudioContableCentral.VigenciaInvitacion }, cancellationToken: ct));
        if (filas == 0) throw new ReglaException("Solo se puede renovar el enlace de una invitación pendiente de esta empresa.");
        var acceso = await central.QuerySingleAsync<EstudioAcceso>(new CommandDefinition(EstudioContableCentral.SelectAcceso + " WHERE a.IdAcceso = @Id;", new { Id = idAcceso }, cancellationToken: ct));
        return new EstudioInvitacionCreada(acceso, Enlace(baseUri, token));
    }, ct);

    public Task RevocarAsync(int idAcceso, CancellationToken ct = default) => EjecutarAsync("Revocar", async () =>
    {
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        var acceso = await central.QuerySingleOrDefaultAsync<EstudioAcceso>(new CommandDefinition(
            EstudioContableCentral.SelectAcceso + " WHERE a.IdAcceso = @Id AND a.IdBase = @IdBase;", new { Id = idAcceso, IdBase = IdBase() }, cancellationToken: ct))
            ?? throw new ReglaException("No se encontró el acceso en esta empresa.");
        if (acceso.Estado == EstudioContableCentral.Revocado) return true;
        // Primero la base (el que corta el ingreso), después la central.
        await using (var cn = new SqlConnection(sessions.GetConnectionString()))
        {
            await cn.OpenAsync(ct);
            await EstudioContableCentral.SincronizarUsuarioBaseAsync(cn, acceso.UsuarioBase, "", activo: false, ct);
        }
        await central.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ESTUDIO_ACCESOS SET Estado = 'REVOCADO', TokenHash = NULL, RevocadoPor = @Por, FechaRevocacion = GETDATE() WHERE IdAcceso = @Id;
            """, new { Id = idAcceso, Por = users.GetCurrentUserName() }, cancellationToken: ct));
        await events.LogAuditAsync("EstudioContable", "Revocar", "ESTUDIO_ACCESOS", idAcceso.ToString(CultureInfo.InvariantCulture),
            $"Revocó el acceso del estudio {acceso.NombreEstudio} ({acceso.EmailInvitado}, usuario {acceso.UsuarioBase}).", new { acceso.IdBase, acceso.EmailInvitado, acceso.UsuarioBase }, CancellationToken.None);
        return true;
    }, ct);

    private async Task<T> EjecutarAsync<T>(string accion, Func<Task<T>> operacion, CancellationToken ct)
    {
        if (!await PuedeAdministrarAsync(ct)) throw new ReglaException("Solo quien administra los usuarios de la empresa puede dar acceso a un estudio.");
        try { return await operacion(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("EstudioContable", accion, ex, "No se pudo administrar el acceso del estudio.", ct: ct);
            throw new AppUserFacingException("No se pudo administrar el acceso del estudio.", codigo, ex);
        }
    }
}

public interface IEstudioPortalService
{
    Task<EstudioInvitacionInfo?> InvitacionAsync(string token, CancellationToken ct = default);
    Task<bool> EsEstudioAsync(CancellationToken ct = default);
    Task AceptarAsync(string token, CancellationToken ct = default);
    /// <summary>Crea la cuenta del estudio (cliente central de tipo E + usuario) y acepta la invitación. Devuelve el login.</summary>
    Task<string> RegistrarYAceptarAsync(string token, EstudioRegistroSolicitud solicitud, CancellationToken ct = default);
    Task<IReadOnlyList<EstudioEmpresa>> EmpresasAsync(DateTime mes, CancellationToken ct = default);
}

/// <summary>Del lado del estudio: aceptar invitaciones y ver sus empresas con el estado del mes.</summary>
public sealed class EstudioPortalService(IConfiguration configuration, IAppUserSessionService users, ICentralBasesService bases,
    ICentralClientesService clientes, IAppEventService events) : IEstudioPortalService
{
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    private sealed class AccesoToken
    {
        public int IdAcceso { get; set; } public int IdBase { get; set; } public string EmailInvitado { get; set; } = "";
        public string UsuarioBase { get; set; } = ""; public string Perfil { get; set; } = ""; public string Estado { get; set; } = ""; public DateTime? TokenVence { get; set; }
    }

    private static async Task<AccesoToken?> PorTokenAsync(SqlConnection central, string token, CancellationToken ct)
        => string.IsNullOrWhiteSpace(token) || token.Length > 64 ? null
            : await central.QuerySingleOrDefaultAsync<AccesoToken>(new CommandDefinition("""
                SELECT IdAcceso, IdBase, EmailInvitado, UsuarioBase, Perfil, Estado, TokenVence FROM dbo.ESTUDIO_ACCESOS WHERE TokenHash = @Hash;
                """, new { Hash = EstudioContableCentral.Hash(token.Trim()) }, cancellationToken: ct));

    public async Task<EstudioInvitacionInfo?> InvitacionAsync(string token, CancellationToken ct = default)
    {
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        var a = await PorTokenAsync(central, token, ct);
        if (a is null) return null;
        var b = await bases.GetByIdAsync(a.IdBase, ct);
        var empresa = b is null ? "" : (await clientes.GetByIdClienteAsync(b.IdCliente, ct))?.RazonSocial ?? "";
        return new EstudioInvitacionInfo(empresa, b?.Nombre ?? "", a.EmailInvitado, a.Perfil, a.Estado, a.TokenVence < DateTime.Now);
    }

    public async Task<bool> EsEstudioAsync(CancellationToken ct = default)
    {
        var u = users.CurrentUser;
        if (u is null || string.IsNullOrWhiteSpace(u.IdCliente)) return false;
        // Se usa en cada login: solo lee Clientes, no crea el esquema.
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct, asegurarEsquema: false);
        return await EsEstudioAsync(central, u.IdCliente, ct);
    }

    private static async Task<bool> EsEstudioAsync(SqlConnection central, string idCliente, CancellationToken ct)
        => await central.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ISNULL(type,'') FROM dbo.Clientes WHERE idcliente = @Id;", new { Id = idCliente }, cancellationToken: ct))
            == EstudioContableCentral.TipoClienteEstudio;

    public async Task AceptarAsync(string token, CancellationToken ct = default)
    {
        var u = users.CurrentUser ?? throw new ReglaException("Ingresá con la cuenta del estudio para aceptar la invitación.");
        var login = (u.CentralLogin ?? u.Email ?? "").Trim();
        if (login.Length == 0) throw new ReglaException("Ingresá con la cuenta del estudio para aceptar la invitación.");
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        if (!await EsEstudioAsync(central, u.IdCliente, ct))
            throw new ReglaException("La cuenta con la que ingresaste es de una empresa, no de un estudio. Creá la cuenta del estudio desde esta misma invitación.");
        await AceptarEnConexionAsync(central, token, u.IdCliente, login, ct);
    }

    private async Task AceptarEnConexionAsync(SqlConnection central, string token, string idCliente, string login, CancellationToken ct)
    {
        var a = await PorTokenAsync(central, token, ct) ?? throw new ReglaException("La invitación no existe o ya se usó.");
        if (a.Estado != EstudioContableCentral.Pendiente) throw new ReglaException("La invitación ya fue aceptada o revocada.");
        if (a.TokenVence < DateTime.Now) throw new ReglaException("La invitación venció. Pedile a la empresa que genere un enlace nuevo.");
        var b = await bases.GetByIdAsync(a.IdBase, ct) ?? throw new ReglaException("La base de la empresa ya no existe.");
        // El usuario de la base pasa a tener el login del estudio: desde ahí AlfaCore entra solo, sin otra contraseña.
        await using (var cn = new SqlConnection(EstudioContableCentral.ConexionBase(b)))
        {
            await cn.OpenAsync(ct);
            var otro = await cn.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT TOP (1) NOMBRE FROM dbo.TA_USUARIOS WHERE email_de = @Login AND NOMBRE <> @Usuario;", new { Login = login, Usuario = a.UsuarioBase }, cancellationToken: ct));
            if (otro is not null) throw new ReglaException($"En esa empresa ya hay otro usuario ({otro.Trim()}) con el email {login}. Pedile a la empresa que lo revise.");
            await EstudioContableCentral.SincronizarUsuarioBaseAsync(cn, a.UsuarioBase, login, activo: true, ct);
        }
        await central.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ESTUDIO_ACCESOS SET Estado = 'ACTIVO', IdClienteEstudio = @IdCliente, LoginEstudio = @Login, FechaAceptacion = GETDATE(), TokenHash = NULL
            WHERE IdAcceso = @Id AND Estado = 'PENDIENTE';
            """, new { Id = a.IdAcceso, IdCliente = idCliente, Login = login }, cancellationToken: ct));
        await events.LogAuditAsync("EstudioContable", "Aceptar", "ESTUDIO_ACCESOS", a.IdAcceso.ToString(CultureInfo.InvariantCulture),
            $"El estudio {login} aceptó el acceso a la base {b.Nombre} (usuario {a.UsuarioBase}).", new { a.IdBase, login, idCliente }, CancellationToken.None);
    }

    public async Task<string> RegistrarYAceptarAsync(string token, EstudioRegistroSolicitud s, CancellationToken ct = default)
    {
        var email = EstudioAccesosService.NormalizarEmail(s.Email);
        var nombre = (s.NombreEstudio ?? "").Trim();
        if (nombre.Length is < 3 or > 150) throw new ReglaException("Ingresá el nombre del estudio.");
        var cuit = new string((s.Cuit ?? "").Where(char.IsDigit).ToArray());
        if (cuit.Length is not (0 or 11)) throw new ReglaException("El CUIT tiene 11 dígitos.");
        if ((s.Password ?? "").Length < 8) throw new ReglaException("La contraseña tiene que tener al menos 8 caracteres.");
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        var a = await PorTokenAsync(central, token, ct) ?? throw new ReglaException("La invitación no existe o ya se usó.");
        if (a.Estado != EstudioContableCentral.Pendiente || a.TokenVence < DateTime.Now) throw new ReglaException("La invitación ya no está vigente.");
        if (await central.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.users WHERE UPPER(LTRIM(RTRIM([user]))) = UPPER(@Email);", new { Email = email }, cancellationToken: ct)) > 0)
            throw new ReglaException("Ya existe una cuenta con ese email: ingresá con ella para aceptar la invitación.");
        await using (var tx = (SqlTransaction)await central.BeginTransactionAsync(ct))
        {
            // Código propio para estudios (EST + número): no se da de alta como cliente en la base comercial de Alfa.
            var idCliente = await central.ExecuteScalarAsync<string>(new CommandDefinition("""
                DECLARE @n int = ISNULL((SELECT MAX(CONVERT(int, SUBSTRING(idcliente, 4, 6))) FROM dbo.Clientes WITH (UPDLOCK, HOLDLOCK)
                                         WHERE idcliente LIKE 'EST[0-9][0-9][0-9][0-9][0-9][0-9]'), 0) + 1;
                SELECT 'EST' + RIGHT('000000' + CONVERT(varchar(6), @n), 6);
                """, transaction: tx, cancellationToken: ct)) ?? throw new InvalidOperationException("No se pudo numerar el estudio.");
            await central.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.Clientes (idcliente, nombre, superadmin, verified, type, created) VALUES (@Id, @Nombre, 0, 1, 'E', GETDATE());
                INSERT INTO dbo.users ([user], password, idcliente, isAdmin, name) VALUES (@Email, @Password, @Id, 1, @NombrePersona);
                """, new { Id = idCliente, Nombre = cuit.Length > 0 ? $"{nombre} ({cuit})" : nombre, Email = email, s.Password,
                    NombrePersona = string.IsNullOrWhiteSpace(s.Nombre) ? nombre[..Math.Min(50, nombre.Length)] : s.Nombre.Trim()[..Math.Min(50, s.Nombre.Trim().Length)] },
                tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            await clientes.GenerateAndSaveIdWebAsync(idCliente, nombre, ct);
            await AceptarEnConexionAsync(central, token, idCliente, email, ct);
        }
        return email;
    }

    public async Task<IReadOnlyList<EstudioEmpresa>> EmpresasAsync(DateTime mes, CancellationToken ct = default)
    {
        var u = users.CurrentUser ?? throw new ReglaException("Ingresá con la cuenta del estudio.");
        await using var central = await EstudioContableCentral.AbrirAsync(configuration, ct);
        var accesos = (await central.QueryAsync<EstudioAcceso>(new CommandDefinition(
            EstudioContableCentral.SelectAcceso + " WHERE a.IdClienteEstudio = @Id AND a.Estado = 'ACTIVO';", new { Id = u.IdCliente }, cancellationToken: ct))).ToList();
        var inicio = new DateTime(mes.Year, mes.Month, 1);
        var lista = new List<EstudioEmpresa>();
        foreach (var a in accesos)
        {
            var b = await bases.GetByIdAsync(a.IdBase, ct);
            if (b is null) continue;
            var c = await clientes.GetByIdClienteAsync(b.IdCliente, ct);
            var idWeb = c?.IdWeb ?? "";
            if (c is not null && string.IsNullOrWhiteSpace(idWeb)) idWeb = await clientes.GenerateAndSaveIdWebAsync(c.IdCliente, c.RazonSocial, ct);
            lista.Add(new EstudioEmpresa
            {
                IdAcceso = a.IdAcceso, IdBase = b.IdBase, Empresa = c?.RazonSocial ?? "", BaseNombre = b.Nombre, IdWebEmpresa = idWeb,
                Perfil = a.Perfil, UsuarioBase = a.UsuarioBase
            });
        }
        // El estado de cada base se lee en paralelo, con un límite: una base caída no frena al resto.
        using var limite = new SemaphoreSlim(6);
        await Task.WhenAll(lista.Select(async e =>
        {
            await limite.WaitAsync(ct);
            try
            {
                var b = await bases.GetByIdAsync(e.IdBase, ct);
                using var corte = CancellationTokenSource.CreateLinkedTokenSource(ct); corte.CancelAfter(TimeSpan.FromSeconds(25));
                e.Estado = await EstadoMesAsync(EstudioContableCentral.ConexionBase(b!), inicio, corte.Token);
            }
            catch (Exception ex) when (ex is SqlException or OperationCanceledException or InvalidOperationException)
            { e.Error = ct.IsCancellationRequested ? "Cancelado" : "No se pudo consultar la base (¿servidor apagado o sin conexión?)."; }
            finally { limite.Release(); }
        }));
        return lista.OrderBy(e => e.Empresa, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.BaseNombre, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static async Task<EstudioEstadoMes> EstadoMesAsync(string conexion, DateTime mes, CancellationToken ct)
    {
        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync(ct);
        var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('CIERREVENTAS','CIERRECOMPRAS','ALFACORE_ARCA_ULTIMA_RECIBIDOS','ALFACORE_ARCA_ULTIMA_EMITIDOS','ALFACORE_PAQUETE_ESTUDIO_ULTIMO');
            """, cancellationToken: ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor);
        DateTime? F(string k) => DateTime.TryParseExact(cfg.GetValueOrDefault(k, ""), ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
        var cantidades = await cn.QuerySingleAsync<(int Ventas, int Compras)>(new CommandDefinition("""
            -- Comprobantes del libro de IVA (la línea marcada no siempre es la primera del asiento, por eso se cuenta por comprobante).
            SELECT COUNT(DISTINCT CASE WHEN LIVA_TIPO = 'VENTAS' THEN TC + SUCURSAL + NUMERO + LETRA + ISNULL(CABCUENTA, CUENTA) END),
                   COUNT(DISTINCT CASE WHEN LIVA_TIPO = 'COMPRAS' THEN TC + SUCURSAL + NUMERO + LETRA + ISNULL(CABCUENTA, CUENTA) END)
            FROM dbo.MV_ASIENTOS WITH (NOLOCK) WHERE LIVA_TIPO IN ('VENTAS', 'COMPRAS') AND FECHA >= @Desde AND FECHA < @Hasta;
            """, new { Desde = mes, Hasta = mes.AddMonths(1) }, commandTimeout: 20, cancellationToken: ct));
        var paquete = cfg.GetValueOrDefault("ALFACORE_PAQUETE_ESTUDIO_ULTIMO", "");
        return new EstudioEstadoMes
        {
            Mes = mes, CierreVentas = F("CIERREVENTAS"), CierreCompras = F("CIERRECOMPRAS"), Ventas = cantidades.Ventas, Compras = cantidades.Compras,
            ControlArcaRecibidos = cfg.GetValueOrDefault("ALFACORE_ARCA_ULTIMA_RECIBIDOS", ""), ControlArcaEmitidos = cfg.GetValueOrDefault("ALFACORE_ARCA_ULTIMA_EMITIDOS", ""),
            // ALFACORE_PAQUETE_ESTUDIO_ULTIMO = "AAAAMM|dd/MM/yyyy HH:mm" (lo graba el paquete del estudio).
            UltimoPaquete = paquete.StartsWith(mes.ToString("yyyyMM", CultureInfo.InvariantCulture) + "|", StringComparison.Ordinal)
                && DateTime.TryParseExact(paquete[7..], "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) ? p : null
        };
    }
}
