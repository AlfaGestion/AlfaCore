using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Net.Http.Json;

namespace AlfaCore.Services;

public sealed class CentralAdminService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppModeService appMode,
    IAppEventService appEvents,
    IConversacionesConfigService conversacionesConfigService,
    IHttpClientFactory httpClientFactory) : ICentralAdminService
{
    private const string AlfaKnowledgeModuloCodigo = "ALFAKNOWLEDGE";

    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<IReadOnlyList<AdminClienteDto>> GetClientesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS RazonSocial,
                ISNULL(idweb, '') AS IdWeb,
                ISNULL(superadmin, 0) AS SuperAdmin
            FROM dbo.Clientes
            ORDER BY ISNULL(nombre, ''), idcliente;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<AdminClienteDto>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToArray();
    }

    public Task<AdminClienteDto?> GetClienteAsync(string idCliente, CancellationToken ct = default)
        => QueryClienteAsync("WHERE idcliente = @IdCliente", new { IdCliente = NormalizeKey(idCliente) }, ct);

    public async Task<IReadOnlyList<ClienteAlfaLookupDto>> SearchVtClientesAsync(string term, int take = 25, CancellationToken ct = default)
    {
        var normalizedTerm = NormalizeText(term);
        if (take <= 0)
            take = 25;

        var sql = $"""
            SELECT TOP ({take})
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(100), CODIGO))), '') AS Codigo,
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(250), RAZON_SOCIAL))), '') AS RazonSocial,
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(50), NUMERO_DOCUMENTO))), '') AS Documento,
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(250), MAIL))), '') AS Mail
            FROM dbo.VT_CLIENTES
            WHERE
                @Term = ''
                OR UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100), CODIGO)))) LIKE @Like
                OR UPPER(LTRIM(RTRIM(CONVERT(nvarchar(250), RAZON_SOCIAL)))) LIKE @Like
                OR UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), NUMERO_DOCUMENTO)))) LIKE @Like
                OR UPPER(LTRIM(RTRIM(CONVERT(nvarchar(250), MAIL)))) LIKE @Like
            ORDER BY
                CASE WHEN UPPER(LTRIM(RTRIM(CONVERT(nvarchar(250), RAZON_SOCIAL)))) LIKE @Prefix THEN 0 ELSE 1 END,
                ISNULL(RAZON_SOCIAL, ''),
                ISNULL(CODIGO, '');
            """;

        try
        {
            var connectionString = sessionService.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new AppUserFacingException("Seleccioná una base activa para buscar clientes de Alfa Gestión.", "ADMIN_VT_CLIENTES_BASE");

            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            var rows = await cn.QueryAsync<ClienteAlfaLookupDto>(new CommandDefinition(sql, new
            {
                Term = normalizedTerm,
                Like = $"%{normalizedTerm.ToUpperInvariant()}%",
                Prefix = $"{normalizedTerm.ToUpperInvariant()}%"
            }, cancellationToken: ct)).ConfigureAwait(false);
            return rows.ToArray();
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                "Central",
                "AdminClienteLookup",
                ex,
                "No se pudo buscar en VT_CLIENTES desde la base activa.",
                new { Term = normalizedTerm, Take = take },
                AppEventSeverity.Error,
                ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task CreateClienteAsync(CrearClienteRequest request, CancellationToken ct = default)
    {
        var normalizedIdCliente = NormalizeKey(request.IdCliente);
        var razonSocial = NormalizeText(request.RazonSocial);
        var idWeb = NormalizeHostWeb(request.IdWeb);
        if (string.IsNullOrWhiteSpace(normalizedIdCliente))
            throw new AppUserFacingException("El ID del cliente es obligatorio.", "ADMIN_CLIENTE_ID");
        if (string.IsNullOrWhiteSpace(razonSocial))
            throw new AppUserFacingException("La razón social es obligatoria.", "ADMIN_CLIENTE_RAZON");
        if (string.IsNullOrWhiteSpace(idWeb))
            throw new AppUserFacingException("El id web es obligatorio.", "ADMIN_CLIENTE_WEB");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (await ExistsClienteAsync(cn, normalizedIdCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe un cliente con ese ID.", "ADMIN_CLIENTE_DUPLICADO");

        if (await ExistsClienteByWebAsync(cn, idWeb, ct).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe un cliente con ese id web.", "ADMIN_CLIENTE_WEB_DUPLICADO");

        var initialPassword = NormalizeText(request.PasswordInicial);
        if (string.IsNullOrWhiteSpace(initialPassword))
            initialPassword = await TryResolveInitialPasswordAsync(normalizedIdCliente, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(initialPassword))
            throw new AppUserFacingException("No se encontró CUIT/documento en la base activa. Cargá la contraseña inicial manualmente.", "ADMIN_CLIENTE_PASSWORD");

        await using var tx = cn.BeginTransaction();
        try
        {
            const string insertClienteSql = """
                INSERT INTO dbo.Clientes (idcliente, nombre, idweb, superadmin)
                VALUES (@IdCliente, @RazonSocial, @IdWeb, @SuperAdmin);
                """;

            await cn.ExecuteAsync(new CommandDefinition(insertClienteSql, new
            {
                IdCliente = normalizedIdCliente,
                RazonSocial = razonSocial,
                IdWeb = idWeb,
                SuperAdmin = request.SuperAdmin ? 1 : 0
            }, tx, cancellationToken: ct)).ConfigureAwait(false);

            await UpsertInitialUserAsync(cn, tx, normalizedIdCliente, normalizedIdCliente, initialPassword, ct).ConfigureAwait(false);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateClienteAsync(CrearClienteRequest request, CancellationToken ct = default)
    {
        var normalizedIdCliente = NormalizeKey(request.IdCliente);
        var razonSocial = NormalizeText(request.RazonSocial);
        var idWeb = NormalizeHostWeb(request.IdWeb);
        if (string.IsNullOrWhiteSpace(normalizedIdCliente))
            throw new AppUserFacingException("El ID del cliente es obligatorio.", "ADMIN_CLIENTE_ID");
        if (string.IsNullOrWhiteSpace(razonSocial))
            throw new AppUserFacingException("La razón social es obligatoria.", "ADMIN_CLIENTE_RAZON");
        if (string.IsNullOrWhiteSpace(idWeb))
            throw new AppUserFacingException("El id web es obligatorio.", "ADMIN_CLIENTE_WEB");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (!await ExistsClienteAsync(cn, normalizedIdCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente no existe.", "ADMIN_CLIENTE_NO_EXISTE");

        if (await ExistsClienteByWebAsync(cn, idWeb, ct, normalizedIdCliente).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe otro cliente con ese id web.", "ADMIN_CLIENTE_WEB_DUPLICADO");

        const string sql = """
            UPDATE dbo.Clientes
            SET nombre = @RazonSocial,
                idweb = @IdWeb,
                superadmin = @SuperAdmin
            WHERE idcliente = @IdCliente;
            """;

        var initialPassword = NormalizeText(request.PasswordInicial);
        await using var tx = cn.BeginTransaction();
        try
        {
            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                IdCliente = normalizedIdCliente,
                RazonSocial = razonSocial,
                IdWeb = idWeb,
                SuperAdmin = request.SuperAdmin ? 1 : 0
            }, tx, cancellationToken: ct)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(initialPassword))
            {
                await UpsertInitialUserAsync(cn, tx, normalizedIdCliente, normalizedIdCliente, initialPassword, ct).ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<string?> TryResolveInitialPasswordAsync(string idCliente, CancellationToken ct = default)
    {
        var connectionString = sessionService.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        const string sql = """
            SELECT TOP (1)
                LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO, ''))) AS PasswordInicial
            FROM dbo.VT_CLIENTES
            WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), CODIGO)))) = UPPER(LTRIM(RTRIM(@IdCliente)));
            """;

        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            var row = await cn.QuerySingleOrDefaultAsync<ClientInitialPasswordRow>(new CommandDefinition(sql, new { IdCliente = NormalizeKey(idCliente) }, cancellationToken: ct)).ConfigureAwait(false);
            return NormalizeText(row?.PasswordInicial);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                "Central",
                "AdminClientePassword",
                ex,
                "No se pudo verificar el CUIT/documento en la base activa.",
                new { IdCliente = NormalizeKey(idCliente) },
                AppEventSeverity.Warning,
                ct).ConfigureAwait(false);
            return null;
        }
    }

    public async Task<IReadOnlyList<AdminBaseDto>> GetBasesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                id AS IdBase,
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS Nombre,
                ISNULL(dbserver, '') AS DbServer,
                ISNULL(dbname, '') AS DbName,
                ISNULL(dbuser, '') AS DbUser,
                ISNULL(dbpassword, '') AS DbPassword
            FROM dbo.bases
            ORDER BY idcliente, ISNULL(nombre, ''), id;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<AdminBaseDto>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToArray();
    }

    public Task<AdminBaseDto?> GetBaseAsync(int idBase, CancellationToken ct = default)
        => QueryBaseAsync("WHERE id = @IdBase", new { IdBase = idBase }, ct);

    public async Task CreateBaseAsync(CrearBaseRequest request, CancellationToken ct = default)
    {
        var normalizedIdCliente = NormalizeKey(request.IdCliente);
        var nombre = NormalizeText(request.Nombre);
        var dbServer = NormalizeText(request.DbServer);
        var dbName = NormalizeText(request.DbName);
        var dbUser = NormalizeText(request.DbUser);
        var dbPassword = request.DbPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedIdCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_BASE_CLIENTE");
        if (!await ExistsClienteAsync(normalizedIdCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente central no existe.", "ADMIN_BASE_CLIENTE_NO_EXISTE");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new AppUserFacingException("El nombre de la base es obligatorio.", "ADMIN_BASE_NOMBRE");
        if (string.IsNullOrWhiteSpace(dbServer))
            throw new AppUserFacingException("El servidor es obligatorio.", "ADMIN_BASE_SERVER");
        if (string.IsNullOrWhiteSpace(dbName))
            throw new AppUserFacingException("El nombre de la base es obligatorio.", "ADMIN_BASE_DBNAME");
        if (string.IsNullOrWhiteSpace(dbUser))
            throw new AppUserFacingException("El usuario SQL es obligatorio.", "ADMIN_BASE_DBUSER");
        if (string.IsNullOrWhiteSpace(dbPassword))
            throw new AppUserFacingException("La contraseña SQL es obligatoria.", "ADMIN_BASE_DBPASSWORD");

        const string sql = """
            INSERT INTO dbo.bases (idcliente, nombre, dbserver, dbname, dbuser, dbpassword)
            VALUES (@IdCliente, @Nombre, @DbServer, @DbName, @DbUser, @DbPassword);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var newId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            IdCliente = normalizedIdCliente,
            Nombre = nombre,
            DbServer = dbServer,
            DbName = dbName,
            DbUser = dbUser,
            DbPassword = dbPassword
        }, cancellationToken: ct)).ConfigureAwait(false);

        if (newId <= 0)
            throw new AppUserFacingException("No se pudo crear la base.", "ADMIN_BASE_INSERT");
    }

    public async Task UpdateBaseAsync(CrearBaseRequest request, CancellationToken ct = default)
    {
        if (request.IdBase is not int idBase || idBase <= 0)
            throw new AppUserFacingException("La base seleccionada es inválida.", "ADMIN_BASE_ID");

        var normalizedIdCliente = NormalizeKey(request.IdCliente);
        var nombre = NormalizeText(request.Nombre);
        var dbServer = NormalizeText(request.DbServer);
        var dbName = NormalizeText(request.DbName);
        var dbUser = NormalizeText(request.DbUser);
        var dbPassword = NormalizeText(request.DbPassword);

        if (string.IsNullOrWhiteSpace(normalizedIdCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_BASE_CLIENTE");
        if (!await ExistsClienteAsync(normalizedIdCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente central no existe.", "ADMIN_BASE_CLIENTE_NO_EXISTE");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new AppUserFacingException("El nombre de la base es obligatorio.", "ADMIN_BASE_NOMBRE");
        if (string.IsNullOrWhiteSpace(dbServer))
            throw new AppUserFacingException("El servidor es obligatorio.", "ADMIN_BASE_SERVER");
        if (string.IsNullOrWhiteSpace(dbName))
            throw new AppUserFacingException("El nombre de la base es obligatorio.", "ADMIN_BASE_DBNAME");
        if (string.IsNullOrWhiteSpace(dbUser))
            throw new AppUserFacingException("El usuario SQL es obligatorio.", "ADMIN_BASE_DBUSER");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var current = await GetBaseAsync(idBase, ct).ConfigureAwait(false);
        if (current is null)
            throw new AppUserFacingException("La base seleccionada no existe.", "ADMIN_BASE_NO_EXISTE");

        if (string.IsNullOrWhiteSpace(dbPassword))
            dbPassword = await GetBasePasswordAsync(cn, idBase, ct).ConfigureAwait(false);

        const string sql = """
            UPDATE dbo.bases
            SET idcliente = @IdCliente,
                nombre = @Nombre,
                dbserver = @DbServer,
                dbname = @DbName,
                dbuser = @DbUser,
                dbpassword = @DbPassword
            WHERE id = @IdBase;
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            IdBase = idBase,
            IdCliente = normalizedIdCliente,
            Nombre = nombre,
            DbServer = dbServer,
            DbName = dbName,
            DbUser = dbUser,
            DbPassword = dbPassword
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteBaseAsync(int idBase, CancellationToken ct = default)
    {
        if (idBase <= 0)
            throw new AppUserFacingException("La base seleccionada es inválida.", "ADMIN_BASE_ID");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var current = await GetBaseAsync(idBase, ct).ConfigureAwait(false);
        if (current is null)
            throw new AppUserFacingException("La base seleccionada no existe.", "ADMIN_BASE_NO_EXISTE");

        const string sql = "DELETE FROM dbo.bases WHERE id = @IdBase;";
        await cn.ExecuteAsync(new CommandDefinition(sql, new { IdBase = idBase }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                u.[user] AS UserName,
                u.idcliente AS IdCliente,
                ISNULL(c.nombre, '') AS RazonSocial,
                ISNULL(u.password, '') AS Password
            FROM dbo.users u
            LEFT JOIN dbo.Clientes c ON c.idcliente = u.idcliente
            ORDER BY u.[user];
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<AdminUserDto>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToArray();
    }

    public Task<AdminUserDto?> GetUserAsync(string userName, CancellationToken ct = default)
        => QueryUserAsync("WHERE u.[user] = @UserName", new { UserName = NormalizeKey(userName) }, ct);

    public async Task CreateUserAsync(CrearUserRequest request, CancellationToken ct = default)
    {
        var userName = NormalizeKey(request.UserName);
        var idCliente = NormalizeKey(request.IdCliente);
        var password = NormalizeText(request.Password);
        if (string.IsNullOrWhiteSpace(userName))
            throw new AppUserFacingException("El usuario es obligatorio.", "ADMIN_USER_NAME");
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_USER_CLIENTE");
        if (!await ExistsClienteAsync(idCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente central no existe.", "ADMIN_USER_CLIENTE_NO_EXISTE");
        if (string.IsNullOrWhiteSpace(password))
            throw new AppUserFacingException("La contraseña es obligatoria.", "ADMIN_USER_PASSWORD");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (await ExistsUserAsync(cn, userName, ct).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe un usuario con ese nombre.", "ADMIN_USER_DUPLICADO");

        const string sql = """
            INSERT INTO dbo.users ([user], password, idcliente)
            VALUES (@UserName, @Password, @IdCliente);
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserName = userName,
            Password = password,
            IdCliente = idCliente
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpdateUserAsync(CrearUserRequest request, CancellationToken ct = default)
    {
        var userName = NormalizeKey(request.UserName);
        var idCliente = NormalizeKey(request.IdCliente);
        var password = NormalizeText(request.Password);
        if (string.IsNullOrWhiteSpace(userName))
            throw new AppUserFacingException("El usuario es obligatorio.", "ADMIN_USER_NAME");
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_USER_CLIENTE");
        if (!await ExistsClienteAsync(idCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente central no existe.", "ADMIN_USER_CLIENTE_NO_EXISTE");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (!await ExistsUserAsync(cn, userName, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El usuario no existe.", "ADMIN_USER_NO_EXISTE");

        if (string.IsNullOrWhiteSpace(password))
        {
            password = await GetUserPasswordAsync(cn, userName, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(password))
                throw new AppUserFacingException("La contraseña es obligatoria.", "ADMIN_USER_PASSWORD");
        }

        const string sql = """
            UPDATE dbo.users
            SET password = @Password,
                idcliente = @IdCliente
            WHERE [user] = @UserName;
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserName = userName,
            Password = password,
            IdCliente = idCliente
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteUserAsync(string userName, CancellationToken ct = default)
    {
        var normalized = NormalizeKey(userName);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new AppUserFacingException("El usuario es obligatorio.", "ADMIN_USER_NAME");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        if (!await ExistsUserAsync(cn, normalized, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El usuario no existe.", "ADMIN_USER_NO_EXISTE");

        const string sql = "DELETE FROM dbo.users WHERE [user] = @UserName;";
        await cn.ExecuteAsync(new CommandDefinition(sql, new { UserName = normalized }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MenuRaizOptionDto>> GetMenuRaizOptionsAsync(CancellationToken ct = default)
    {
        var defaultConnectionString = configuration.GetConnectionString("AlfaGestion")
            ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

        const string sql = """
            ;WITH nodos AS (
                SELECT
                    w.Clave,
                    w.PadreClave,
                    ISNULL(NULLIF(w.NombreWeb, ''), w.Clave) AS Nombre,
                    CAST(ISNULL(NULLIF(w.NombreWeb, ''), w.Clave) AS nvarchar(400)) AS Ruta,
                    0 AS Nivel
                FROM dbo.ALFACORE_MENU_WEB w
                WHERE UPPER(LTRIM(RTRIM(ISNULL(w.Menu, '')))) = N'ALFA'
                  AND (w.PadreClave IS NULL OR LTRIM(RTRIM(w.PadreClave)) = '')
                  AND ISNULL(w.Clave, '') <> ''

                UNION ALL

                SELECT
                    w.Clave,
                    w.PadreClave,
                    ISNULL(NULLIF(w.NombreWeb, ''), w.Clave),
                    CAST(n.Ruta + N' > ' + ISNULL(NULLIF(w.NombreWeb, ''), w.Clave) AS nvarchar(400)),
                    n.Nivel + 1
                FROM dbo.ALFACORE_MENU_WEB w
                JOIN nodos n ON w.PadreClave = n.Clave
                WHERE UPPER(LTRIM(RTRIM(ISNULL(w.Menu, '')))) = N'ALFA'
                  AND ISNULL(w.Clave, '') <> ''
            )
            SELECT Clave, Ruta AS Nombre
            FROM nodos
            ORDER BY Ruta;
            """;

        await using var cn = new SqlConnection(defaultConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<MenuRaizOptionDto>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToArray();
    }

    public async Task<IReadOnlyList<ModuloDto>> GetModulosAsync(CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await QueryModulosAsync(cn, ct).ConfigureAwait(false);
    }

    public async Task<ModuloDto?> GetModuloAsync(int idModulo, CancellationToken ct = default)
    {
        var modulos = await GetModulosAsync(ct).ConfigureAwait(false);
        return modulos.FirstOrDefault(m => m.Id == idModulo);
    }

    public async Task CreateModuloAsync(CrearModuloRequest request, CancellationToken ct = default)
    {
        var codigo = NormalizeKey(request.Codigo).ToUpperInvariant();
        var nombre = NormalizeText(request.Nombre);
        var menuKeyRaiz = NormalizeKey(request.MenuKeyRaiz);

        if (string.IsNullOrWhiteSpace(codigo))
            throw new AppUserFacingException("El código del módulo es obligatorio.", "ADMIN_MODULO_CODIGO");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new AppUserFacingException("El nombre del módulo es obligatorio.", "ADMIN_MODULO_NOMBRE");
        // MenuKeyRaiz es opcional: hay módulos "solo funcionalidad" que no agregan una sección
        // propia al menú (ej.: AlfaKnowledge, un panel dentro de Conversaciones).

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (await ExistsModuloCodigoAsync(cn, codigo, null, ct).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe un módulo con ese código.", "ADMIN_MODULO_DUPLICADO");

        await using var tx = cn.BeginTransaction();
        try
        {
            const string insertSql = """
                INSERT INTO dbo.Modulos (Codigo, Nombre, Descripcion, MenuKeyRaiz, Precio, Activo)
                VALUES (@Codigo, @Nombre, @Descripcion, @MenuKeyRaiz, @Precio, 1);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            var newId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
            {
                Codigo = codigo,
                Nombre = nombre,
                Descripcion = string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim(),
                MenuKeyRaiz = string.IsNullOrWhiteSpace(menuKeyRaiz) ? null : menuKeyRaiz,
                request.Precio
            }, tx, cancellationToken: ct)).ConfigureAwait(false);

            await ReplaceDependenciasAsync(cn, tx, newId, request.Dependencias, ct).ConfigureAwait(false);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateModuloAsync(CrearModuloRequest request, CancellationToken ct = default)
    {
        if (request.Id is not int id || id <= 0)
            throw new AppUserFacingException("El módulo seleccionado es inválido.", "ADMIN_MODULO_ID");

        var codigo = NormalizeKey(request.Codigo).ToUpperInvariant();
        var nombre = NormalizeText(request.Nombre);
        var menuKeyRaiz = NormalizeKey(request.MenuKeyRaiz);

        if (string.IsNullOrWhiteSpace(codigo))
            throw new AppUserFacingException("El código del módulo es obligatorio.", "ADMIN_MODULO_CODIGO");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new AppUserFacingException("El nombre del módulo es obligatorio.", "ADMIN_MODULO_NOMBRE");
        // MenuKeyRaiz es opcional: hay módulos "solo funcionalidad" que no agregan una sección
        // propia al menú (ej.: AlfaKnowledge, un panel dentro de Conversaciones).

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        if (await ExistsModuloCodigoAsync(cn, codigo, id, ct).ConfigureAwait(false))
            throw new AppUserFacingException("Ya existe otro módulo con ese código.", "ADMIN_MODULO_DUPLICADO");

        await using var tx = cn.BeginTransaction();
        try
        {
            const string sql = """
                UPDATE dbo.Modulos
                SET Codigo = @Codigo,
                    Nombre = @Nombre,
                    Descripcion = @Descripcion,
                    MenuKeyRaiz = @MenuKeyRaiz,
                    Precio = @Precio
                WHERE Id = @Id;
                """;

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = id,
                Codigo = codigo,
                Nombre = nombre,
                Descripcion = string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim(),
                MenuKeyRaiz = string.IsNullOrWhiteSpace(menuKeyRaiz) ? null : menuKeyRaiz,
                request.Precio
            }, tx, cancellationToken: ct)).ConfigureAwait(false);

            await ReplaceDependenciasAsync(cn, tx, id, request.Dependencias, ct).ConfigureAwait(false);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteModuloAsync(int idModulo, CancellationToken ct = default)
    {
        if (idModulo <= 0)
            throw new AppUserFacingException("El módulo seleccionado es inválido.", "ADMIN_MODULO_ID");

        // Baja lógica: un DELETE físico chocaría con la FK de ClienteModulos/ModulosDependencias
        // en cuanto algún cliente lo tenga activo o algún otro módulo dependa de él.
        const string sql = "UPDATE dbo.Modulos SET Activo = 0 WHERE Id = @Id;";
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { Id = idModulo }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClienteModuloDto>> GetClienteModulosAsync(string idCliente, CancellationToken ct = default)
    {
        var normalizedIdCliente = NormalizeKey(idCliente);
        if (string.IsNullOrWhiteSpace(normalizedIdCliente))
            return [];

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var modulos = await QueryModulosAsync(cn, ct).ConfigureAwait(false);
        var esLegacy = await IsClienteLegacyAsync(cn, normalizedIdCliente, ct).ConfigureAwait(false);

        const string activosSql = """
            SELECT IdModulo, Estado, ActivadoUtc, ActivadoPor, SolicitadoUtc, SolicitadoPor, PruebaVenceUtc
            FROM dbo.ClienteModulos
            WHERE IdCliente = @IdCliente;
            """;
        var activos = (await cn.QueryAsync<ClienteModuloRow>(new CommandDefinition(activosSql, new { IdCliente = normalizedIdCliente }, cancellationToken: ct)).ConfigureAwait(false))
            .ToDictionary(x => x.IdModulo);

        const string dependenciaSql = "SELECT DISTINCT IdModuloDependeDe FROM dbo.ModulosDependencias;";
        var esDependenciaSet = (await cn.QueryAsync<int>(new CommandDefinition(dependenciaSql, cancellationToken: ct)).ConfigureAwait(false)).ToHashSet();

        return modulos
            .Where(m => m.Activo)
            .Select(m =>
            {
                activos.TryGetValue(m.Id, out var activoRow);
                var estaActivo = esLegacy
                    || string.Equals(activoRow?.Estado, ClienteModuloEstados.Activo, StringComparison.OrdinalIgnoreCase)
                    || EstaEnPruebaVigente(activoRow?.Estado, activoRow?.PruebaVenceUtc);

                return new ClienteModuloDto
                {
                    IdModulo = m.Id,
                    Codigo = m.Codigo,
                    Nombre = m.Nombre,
                    Precio = m.Precio,
                    EsDependenciaDeOtro = esDependenciaSet.Contains(m.Id),
                    EstaActivo = estaActivo,
                    Estado = esLegacy ? ClienteModuloEstados.Activo : (activoRow?.Estado ?? string.Empty),
                    ActivadoUtc = activoRow?.ActivadoUtc,
                    ActivadoPor = activoRow?.ActivadoPor,
                    SolicitadoUtc = activoRow?.SolicitadoUtc,
                    SolicitadoPor = activoRow?.SolicitadoPor,
                    PruebaVenceUtc = activoRow?.PruebaVenceUtc
                };
            })
            .OrderBy(x => x.Nombre)
            .ToArray();
    }

    public async Task ActivarModuloAsync(ActivarModuloRequest request, CancellationToken ct = default)
    {
        var idCliente = NormalizeKey(request.IdCliente);
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_CLIENTEMODULO_CLIENTE");
        if (!await ExistsClienteAsync(idCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente central no existe.", "ADMIN_CLIENTEMODULO_CLIENTE_NO_EXISTE");

        await ActivarConEstadoAsync(idCliente, request.IdModulo, ClienteModuloEstados.Activo, NormalizeText(request.ActivadoPor), pruebaVenceUtc: null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Autoservicio: arranca la prueba gratuita de <see cref="PruebaModuloDefaults.DiasDuracion"/>
    /// días para los módulos elegidos al confirmar el registro público. Las dependencias
    /// obligatorias (Clientes/Técnicos) se activan directo como <c>Activo</c> sin cargo, igual
    /// que en <see cref="ActivarModuloAsync"/> — solo el/los módulo(s) elegidos por el cliente
    /// quedan en <c>Prueba</c> con vencimiento. No baja de categoría un módulo que ya esté
    /// <c>Activo</c> (pagado) para ese cliente.
    /// </summary>
    public async Task IniciarPruebaModulosAsync(IniciarPruebaModulosRequest request, CancellationToken ct = default)
    {
        var idCliente = NormalizeKey(request.IdCliente);
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_CLIENTEMODULO_CLIENTE");
        if (!await ExistsClienteAsync(idCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente central no existe.", "ADMIN_CLIENTEMODULO_CLIENTE_NO_EXISTE");

        var idsElegidos = (request.IdsModulos ?? []).Distinct().ToArray();
        if (idsElegidos.Length == 0)
            return;

        // Cada módulo se activa de forma independiente: si a uno le falla el aprovisionamiento
        // externo (hoy solo AlfaKnowledge llama a otro servicio), el módulo en sí queda igual en
        // Prueba (el commit a ClienteModulos ya pasó antes de intentar aprovisionar) y el resto de
        // los módulos elegidos no se ven afectados. Recién al final, si hubo fallas, se informa
        // cuáles para que el usuario sepa que esos puntualmente necesitan reintentarse.
        var pruebaVenceUtc = DateTime.UtcNow.AddDays(PruebaModuloDefaults.DiasDuracion);
        var fallas = new List<string>();
        foreach (var idModulo in idsElegidos)
        {
            try
            {
                await ActivarConEstadoAsync(idCliente, idModulo, ClienteModuloEstados.Prueba, activadoPor: "auto-registro", pruebaVenceUtc, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await appEvents.LogErrorAsync(
                    "Central",
                    "IniciarPruebaModulo",
                    ex,
                    "Un módulo de la prueba gratuita autoservicio no se pudo activar del todo.",
                    new { idCliente, idModulo },
                    AppEventSeverity.Warning,
                    ct).ConfigureAwait(false);
                fallas.Add(ex.Message);
            }
        }

        if (fallas.Count > 0)
        {
            throw new AppUserFacingException(
                $"Se activó la prueba de los módulos elegidos, pero hubo un problema con {fallas.Count} de ellos: {string.Join(" | ", fallas)}",
                "ADMIN_PRUEBA_MODULOS_PARCIAL");
        }
    }

    /// <summary>
    /// Núcleo compartido por <see cref="ActivarModuloAsync"/> (activación real, pagada) y
    /// <see cref="IniciarPruebaModulosAsync"/> (prueba gratuita autoservicio): resuelve la
    /// cascada de dependencias obligatorias (siempre se activan como <c>Activo</c>, sin cargo),
    /// aplica el estado pedido al módulo elegido y dispara el aprovisionamiento de AlfaKnowledge
    /// si corresponde. No baja de categoría un módulo que ya esté <c>Activo</c> para ese cliente
    /// (evita que una prueba iniciada por error retroceda un contrato pago).
    /// </summary>
    private async Task ActivarConEstadoAsync(string idCliente, int idModulo, string estado, string? activadoPor, DateTime? pruebaVenceUtc, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var modulos = await QueryModulosAsync(cn, ct).ConfigureAwait(false);
        if (modulos.All(m => m.Id != idModulo || !m.Activo))
            throw new AppUserFacingException("El módulo seleccionado no existe o está dado de baja.", "ADMIN_CLIENTEMODULO_MODULO_NO_EXISTE");

        // Las dependencias se activan en cascada junto con el módulo pedido, sin cargo aparte
        // (decisión de producto — ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md).
        var idsAActivar = ResolveDependenciasTransitivas(modulos, idModulo);

        const string estadoActualSql = "SELECT Estado FROM dbo.ClienteModulos WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo;";
        const string upsertSql = """
            IF EXISTS (SELECT 1 FROM dbo.ClienteModulos WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo)
                UPDATE dbo.ClienteModulos
                SET Estado = @Estado, ActivadoUtc = GETUTCDATE(), ActivadoPor = @ActivadoPor, PruebaVenceUtc = @PruebaVenceUtc
                WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo;
            ELSE
                INSERT INTO dbo.ClienteModulos (IdCliente, IdModulo, Estado, ActivadoPor, PruebaVenceUtc)
                VALUES (@IdCliente, @IdModulo, @Estado, @ActivadoPor, @PruebaVenceUtc);
            """;

        await using var tx = cn.BeginTransaction();
        try
        {
            foreach (var id in idsAActivar)
            {
                // El módulo pedido directamente recibe el estado pedido (Activo o Prueba); las
                // dependencias arrastradas siempre van directo a Activo sin cargo.
                var esModuloDirecto = id == idModulo;
                var estadoAAplicar = esModuloDirecto ? estado : ClienteModuloEstados.Activo;
                var venceAAplicar = esModuloDirecto ? pruebaVenceUtc : null;

                var estadoActual = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(estadoActualSql, new { IdCliente = idCliente, IdModulo = id }, tx, cancellationToken: ct)).ConfigureAwait(false);
                if (string.Equals(estadoActual, ClienteModuloEstados.Activo, StringComparison.OrdinalIgnoreCase))
                    continue; // ya es un contrato pago activo — nunca lo retrocedemos a Prueba.

                await cn.ExecuteAsync(new CommandDefinition(upsertSql, new
                {
                    IdCliente = idCliente,
                    IdModulo = id,
                    Estado = estadoAAplicar,
                    ActivadoPor = string.IsNullOrWhiteSpace(activadoPor) ? null : activadoPor,
                    PruebaVenceUtc = venceAAplicar
                }, tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        // Si el módulo activado directamente (no una dependencia arrastrada) es AlfaKnowledge,
        // aprovisionamos su base de conocimiento en el momento del contrato (o de la prueba) en
        // vez de dejarlo para carga manual — ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md.
        var moduloActivado = modulos.FirstOrDefault(m => m.Id == idModulo);
        if (moduloActivado is not null && string.Equals(moduloActivado.Codigo, AlfaKnowledgeModuloCodigo, StringComparison.OrdinalIgnoreCase))
        {
            await ProvisionAlfaKnowledgeAsync(idCliente, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Crea (si todavía no existe) la base de conocimiento del cliente en AlfaKnowledge y guarda
    /// la conexión resultante en su propia base (TA_CONFIGURACION), sin depender de que el
    /// superadmin tenga esa base como sesión activa. No aborta la activación del módulo si algo
    /// sale mal — el módulo queda activo igual, y esto se puede reintentar volviendo a activar.
    /// </summary>
    private async Task ProvisionAlfaKnowledgeAsync(string idCliente, CancellationToken ct)
    {
        var tenantConnectionString = await ResolveClienteConnectionStringAsync(idCliente, ct).ConfigureAwait(false);
        if (tenantConnectionString is null)
        {
            throw new AppUserFacingException(
                "El módulo AlfaKnowledge quedó activo, pero el cliente todavía no tiene una base de ERP registrada para guardar la conexión. Cargala en Administrar y volvé a activar el módulo.",
                "ADMIN_ALFAKNOWLEDGE_SIN_BASE");
        }

        if (await TieneAlfaKnowledgeConfiguradoAsync(tenantConnectionString, ct).ConfigureAwait(false))
            return;

        var baseUrl = configuration["AlfaKnowledge:BaseUrl"];
        var externalApiKey = configuration["AlfaKnowledge:ExternalApiKey"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(externalApiKey))
        {
            throw new AppUserFacingException(
                "El módulo AlfaKnowledge quedó activo, pero falta configurar AlfaKnowledge:BaseUrl / AlfaKnowledge:ExternalApiKey en el servidor para poder aprovisionar la base automáticamente.",
                "ADMIN_ALFAKNOWLEDGE_SIN_CONFIG");
        }

        var databaseName = BuildAlfaKnowledgeDatabaseName(idCliente);

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("X-Api-Key", externalApiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                $"{baseUrl.TrimEnd('/')}/api/external/knowledge-bases",
                new { name = $"Cliente {idCliente}", databaseName, qdrantCollectionName = (string?)null, setAsDefault = false },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppUserFacingException(
                "El módulo AlfaKnowledge quedó activo, pero no se pudo contactar al servidor de AlfaKnowledge para crear la base. Reintentá más tarde volviendo a activar el módulo.",
                "ADMIN_ALFAKNOWLEDGE_SIN_CONEXION",
                ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new AppUserFacingException(
                $"El módulo AlfaKnowledge quedó activo, pero AlfaKnowledge rechazó la creación de la base ({(int)response.StatusCode}). Detalle: {detail}",
                "ADMIN_ALFAKNOWLEDGE_PROVISION_FALLO");
        }

        var payload = await response.Content.ReadFromJsonAsync<AlfaKnowledgeProvisionResponse>(cancellationToken: ct).ConfigureAwait(false);
        var knowledgeBaseId = payload?.KnowledgeBase?.Id;
        if (knowledgeBaseId is null)
        {
            throw new AppUserFacingException(
                "El módulo AlfaKnowledge quedó activo, pero la respuesta de AlfaKnowledge no incluyó el id de la base creada.",
                "ADMIN_ALFAKNOWLEDGE_RESPUESTA_INVALIDA");
        }

        await conversacionesConfigService.SaveAlfaKnowledgeConfigForConnectionAsync(
            tenantConnectionString,
            new ConversacionAlfaKnowledgeConfigDto
            {
                BaseUrl = baseUrl,
                ApiKey = externalApiKey,
                KnowledgeBaseId = knowledgeBaseId.Value.ToString(),
                TimeoutSeconds = 15
            },
            ct).ConfigureAwait(false);
    }

    private async Task<bool> TieneAlfaKnowledgeConfiguradoAsync(string tenantConnectionString, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) VALOR
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CONV_ALFAKNOWLEDGE_KNOWLEDGE_BASE_ID';
            """;

        await using var cn = new SqlConnection(tenantConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var value = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Cliente dueño de la base actualmente activa en la sesión — NO el cliente de la cuenta
    /// central con la que se logueó. Para un superadmin de Alfa parado en la base de otro
    /// cliente (caso normal en Administrar), son cosas distintas: <c>appUserSession.CurrentUser.IdCliente</c>
    /// es "quién soy yo", esto es "de qué cliente son los datos que estoy mirando ahora" — lo
    /// segundo es lo que hay que usar para filtrar módulos/menú. Cae a
    /// <c>appUserSession.CurrentUser.IdCliente</c> si todavía no hay una base activa resuelta.
    /// </summary>
    private async Task<string?> ResolveIdClienteDeBaseActivaAsync(CancellationToken ct)
    {
        var activeSession = sessionService.GetActiveSession();
        if (activeSession is not null && activeSession.BaseId > 0)
        {
            const string sql = "SELECT idcliente FROM dbo.bases WHERE id = @IdBase;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            var idClienteDeBase = await cn.ExecuteScalarAsync<string?>(
                new CommandDefinition(sql, new { IdBase = activeSession.BaseId }, cancellationToken: ct)).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(idClienteDeBase))
                return idClienteDeBase;
        }

        return appUserSession.CurrentUser?.IdCliente;
    }

    private async Task<string?> ResolveClienteConnectionStringAsync(string idCliente, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                id AS IdBase,
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS Nombre,
                ISNULL(dbserver, '') AS DbServer,
                ISNULL(dbname, '') AS DbName,
                ISNULL(dbuser, '') AS DbUser,
                ISNULL(dbpassword, '') AS DbPassword
            FROM dbo.bases
            WHERE idcliente = @IdCliente
            ORDER BY id;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var row = await cn.QuerySingleOrDefaultAsync<AdminBaseDto>(
            new CommandDefinition(sql, new { IdCliente = idCliente }, cancellationToken: ct)).ConfigureAwait(false);

        if (row is null || string.IsNullOrWhiteSpace(row.DbServer) || string.IsNullOrWhiteSpace(row.DbName))
            return null;

        return new SqlConnectionStringBuilder
        {
            DataSource = row.DbServer,
            InitialCatalog = row.DbName,
            UserID = row.DbUser,
            Password = row.DbPassword,
            TrustServerCertificate = true,
            ApplicationName = "AlfaCore"
        }.ConnectionString;
    }

    private static string BuildAlfaKnowledgeDatabaseName(string idCliente)
    {
        var sanitized = new string(idCliente.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return $"AK_{sanitized}";
    }

    private sealed class AlfaKnowledgeProvisionResponse
    {
        public AlfaKnowledgeProvisionKnowledgeBase? KnowledgeBase { get; set; }
    }

    private sealed class AlfaKnowledgeProvisionKnowledgeBase
    {
        public Guid Id { get; set; }
    }

    public async Task SuspenderModuloAsync(string idCliente, int idModulo, CancellationToken ct = default)
    {
        var normalizedIdCliente = NormalizeKey(idCliente);
        if (string.IsNullOrWhiteSpace(normalizedIdCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_CLIENTEMODULO_CLIENTE");

        const string sql = """
            UPDATE dbo.ClienteModulos
            SET Estado = N'Suspendido'
            WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { IdCliente = normalizedIdCliente, IdModulo = idModulo }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Pruebas gratuitas vigentes que vencen dentro de <paramref name="diasAntes"/> días y todavía
    /// no recibieron un aviso en las últimas 24hs — para el job de recordatorios. El email sale
    /// de <c>dbo.users</c> (mismo login con el que el cliente se registró), no de <c>dbo.Clientes</c>
    /// (que no tiene columna de email).
    /// </summary>
    public async Task<IReadOnlyList<PruebaModuloRecordatorioDto>> GetPruebasPorVencerAsync(int diasAntes, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                cm.IdCliente,
                ISNULL(c.nombre, '') AS ClienteNombre,
                ISNULL(u.[user], '') AS Email,
                cm.IdModulo,
                m.Codigo,
                m.Nombre,
                m.Precio,
                cm.PruebaVenceUtc
            FROM dbo.ClienteModulos cm
            JOIN dbo.Modulos m ON m.Id = cm.IdModulo
            JOIN dbo.Clientes c ON c.idcliente = cm.IdCliente
            LEFT JOIN dbo.users u ON u.idcliente = cm.IdCliente
            WHERE cm.Estado = N'Prueba'
              AND cm.PruebaVenceUtc IS NOT NULL
              AND cm.PruebaVenceUtc > GETUTCDATE()
              AND cm.PruebaVenceUtc <= DATEADD(day, @DiasAntes, GETUTCDATE())
              AND (cm.UltimoRecordatorioUtc IS NULL OR cm.UltimoRecordatorioUtc < DATEADD(hour, -24, GETUTCDATE()))
            ORDER BY cm.PruebaVenceUtc;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<PruebaModuloRecordatorioDto>(new CommandDefinition(sql, new { DiasAntes = diasAntes }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.Where(r => !string.IsNullOrWhiteSpace(r.Email)).ToArray();
    }

    public async Task MarcarRecordatorioPruebaEnviadoAsync(string idCliente, int idModulo, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.ClienteModulos
            SET UltimoRecordatorioUtc = GETUTCDATE()
            WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { IdCliente = idCliente, IdModulo = idModulo }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Pasa a <c>Suspendido</c> toda prueba vencida que nadie convirtió en contrato pago. Se
    /// llama desde el job diario, antes de mandar los recordatorios del día — así una prueba que
    /// venció esta noche ya no aparece como "vigente" en ninguna pantalla al día siguiente.
    /// </summary>
    public async Task<int> ExpirarPruebasVencidasAsync(CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.ClienteModulos
            SET Estado = N'Suspendido'
            WHERE Estado = N'Prueba' AND PruebaVenceUtc IS NOT NULL AND PruebaVenceUtc <= GETUTCDATE();
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await cn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SolicitudModuloDto>> GetSolicitudesPendientesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                cm.IdCliente,
                ISNULL(c.nombre, '') AS ClienteNombre,
                cm.IdModulo,
                m.Codigo,
                m.Nombre,
                m.Precio,
                cm.SolicitadoUtc,
                cm.SolicitadoPor
            FROM dbo.ClienteModulos cm
            JOIN dbo.Modulos m ON m.Id = cm.IdModulo
            JOIN dbo.Clientes c ON c.idcliente = cm.IdCliente
            WHERE cm.Estado = N'Solicitado'
            ORDER BY cm.SolicitadoUtc;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<SolicitudModuloDto>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToArray();
    }

    public async Task SolicitarModuloAsync(SolicitarModuloRequest request, CancellationToken ct = default)
    {
        var idCliente = NormalizeKey(request.IdCliente);
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_CLIENTEMODULO_CLIENTE");
        if (!await ExistsClienteAsync(idCliente, ct).ConfigureAwait(false))
            throw new AppUserFacingException("El cliente central no existe.", "ADMIN_CLIENTEMODULO_CLIENTE_NO_EXISTE");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var modulos = await QueryModulosAsync(cn, ct).ConfigureAwait(false);
        if (modulos.All(m => m.Id != request.IdModulo || !m.Activo))
            throw new AppUserFacingException("El módulo seleccionado no existe o está dado de baja.", "ADMIN_CLIENTEMODULO_MODULO_NO_EXISTE");

        var solicitadoPor = NormalizeText(request.SolicitadoPor);

        // El WHERE Estado <> 'Activo' en la rama UPDATE evita que "Solicitar" por error sobre un
        // módulo ya activo lo haga retroceder a Solicitado.
        const string upsertSql = """
            IF EXISTS (SELECT 1 FROM dbo.ClienteModulos WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo)
                UPDATE dbo.ClienteModulos
                SET Estado = N'Solicitado', SolicitadoUtc = GETUTCDATE(), SolicitadoPor = @SolicitadoPor,
                    DecididoUtc = NULL, DecididoPor = NULL
                WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo AND Estado <> N'Activo';
            ELSE
                INSERT INTO dbo.ClienteModulos (IdCliente, IdModulo, Estado, SolicitadoUtc, SolicitadoPor)
                VALUES (@IdCliente, @IdModulo, N'Solicitado', GETUTCDATE(), @SolicitadoPor);
            """;

        await cn.ExecuteAsync(new CommandDefinition(upsertSql, new
        {
            IdCliente = idCliente,
            IdModulo = request.IdModulo,
            SolicitadoPor = string.IsNullOrWhiteSpace(solicitadoPor) ? null : solicitadoPor
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RechazarModuloAsync(RechazarModuloRequest request, CancellationToken ct = default)
    {
        var idCliente = NormalizeKey(request.IdCliente);
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "ADMIN_CLIENTEMODULO_CLIENTE");

        var decididoPor = NormalizeText(request.DecididoPor);

        const string sql = """
            UPDATE dbo.ClienteModulos
            SET Estado = N'Rechazado', DecididoUtc = GETUTCDATE(), DecididoPor = @DecididoPor
            WHERE IdCliente = @IdCliente AND IdModulo = @IdModulo AND Estado = N'Solicitado';
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            IdCliente = idCliente,
            IdModulo = request.IdModulo,
            DecididoPor = string.IsNullOrWhiteSpace(decididoPor) ? null : decididoPor
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> IsModuloActivoParaClienteActualAsync(string codigoModulo, CancellationToken ct = default)
    {
        // Instalación clásica de un solo cliente (on-premise, sin ALFA_CENTRAL de por medio):
        // no hay noción de "módulo contratado", todo sigue disponible como siempre.
        if (!appMode.IsSaaSMode)
            return true;

        var idCliente = await ResolveIdClienteDeBaseActivaAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(idCliente))
            return true;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);

            if (await IsClienteLegacyAsync(cn, idCliente, ct).ConfigureAwait(false))
                return true;

            // Si el módulo todavía no está definido en el catálogo, nadie configuró esta
            // integración opcional todavía — no bloqueamos por eso (fail-open).
            const string existsSql = """
                SELECT COUNT(1) FROM dbo.Modulos
                WHERE UPPER(LTRIM(RTRIM(Codigo))) = UPPER(LTRIM(RTRIM(@Codigo))) AND Activo = 1;
                """;
            var moduloExiste = await cn.ExecuteScalarAsync<int>(new CommandDefinition(existsSql, new { Codigo = codigoModulo }, cancellationToken: ct)).ConfigureAwait(false) > 0;
            if (!moduloExiste)
                return true;

            const string activoSql = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.ClienteModulos cm
                    INNER JOIN dbo.Modulos m ON m.Id = cm.IdModulo
                    WHERE UPPER(LTRIM(RTRIM(cm.IdCliente))) = UPPER(LTRIM(RTRIM(@IdCliente)))
                      AND UPPER(LTRIM(RTRIM(m.Codigo))) = UPPER(LTRIM(RTRIM(@Codigo)))
                      AND (cm.Estado = N'Activo' OR (cm.Estado = N'Prueba' AND (cm.PruebaVenceUtc IS NULL OR cm.PruebaVenceUtc > GETUTCDATE())))
                ) THEN 1 ELSE 0 END;
                """;
            return await cn.ExecuteScalarAsync<bool>(new CommandDefinition(activoSql, new { IdCliente = idCliente, Codigo = codigoModulo }, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                "Central",
                "ModuloActivoCheck",
                ex,
                "No se pudo verificar si un módulo opcional está activo; se muestra por defecto.",
                new { idCliente, codigoModulo },
                AppEventSeverity.Warning,
                ct).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// Para <c>MenuService</c>: qué nodos raíz del menú caen bajo el sistema de módulos para el
    /// cliente del usuario logueado. Devuelve <c>null</c> cuando NO corresponde filtrar el menú
    /// (modo legacy/on-premise o cliente legacy) — en ese caso el menú se arma completo, como
    /// siempre. Cuando devuelve un valor, cualquier nodo cuya clave (o la de algún ancestro) no
    /// figure en <see cref="ModuloMenuFiltroDto.MenuKeysActivos"/> se oculta — a diferencia de
    /// <see cref="IsModuloActivoParaClienteActualAsync"/>, acá una sección SIN módulo definido
    /// también se oculta (decisión de producto: el menú solo muestra lo que el cliente contrató).
    /// </summary>
    public async Task<ModuloMenuFiltroDto?> GetModuloMenuFiltroParaClienteActualAsync(CancellationToken ct = default)
    {
        if (!appMode.IsSaaSMode)
        {
            await appEvents.LogAuditAsync("Central", "ModuloMenuFiltroCheck", "Modulos", "-", "Sin filtrar: IsSaaSMode=false.", null, ct).ConfigureAwait(false);
            return null;
        }

        var idCliente = await ResolveIdClienteDeBaseActivaAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(idCliente))
        {
            await appEvents.LogAuditAsync("Central", "ModuloMenuFiltroCheck", "Modulos", "-", "Sin filtrar: IdCliente vacío en la sesión actual.", null, ct).ConfigureAwait(false);
            return null;
        }

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);

            if (await IsClienteLegacyAsync(cn, idCliente, ct).ConfigureAwait(false))
            {
                await appEvents.LogAuditAsync("Central", "ModuloMenuFiltroCheck", "Modulos", idCliente, "Sin filtrar: cliente legacy.", null, ct).ConfigureAwait(false);
                return null;
            }

            const string sql = """
                SELECT m.MenuKeyRaiz, cm.Estado, cm.PruebaVenceUtc
                FROM dbo.Modulos m
                LEFT JOIN dbo.ClienteModulos cm
                    ON cm.IdModulo = m.Id AND UPPER(LTRIM(RTRIM(cm.IdCliente))) = UPPER(LTRIM(RTRIM(@IdCliente)))
                WHERE m.Activo = 1 AND m.MenuKeyRaiz IS NOT NULL AND LTRIM(RTRIM(m.MenuKeyRaiz)) <> '';
                """;

            var rows = (await cn.QueryAsync<(string MenuKeyRaiz, string? Estado, DateTime? PruebaVenceUtc)>(
                new CommandDefinition(sql, new { IdCliente = idCliente }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

            var definidos = new HashSet<string>(rows.Select(r => r.MenuKeyRaiz.Trim()), StringComparer.OrdinalIgnoreCase);
            var activos = new HashSet<string>(
                rows.Where(r => string.Equals(r.Estado, ClienteModuloEstados.Activo, StringComparison.OrdinalIgnoreCase) || EstaEnPruebaVigente(r.Estado, r.PruebaVenceUtc))
                    .Select(r => r.MenuKeyRaiz.Trim()),
                StringComparer.OrdinalIgnoreCase);

            await appEvents.LogAuditAsync(
                "Central",
                "ModuloMenuFiltroCheck",
                "Modulos",
                idCliente,
                "Filtro calculado.",
                new { idCliente, definidos = definidos.ToArray(), activos = activos.ToArray() },
                ct).ConfigureAwait(false);

            return new ModuloMenuFiltroDto { MenuKeysDefinidos = definidos, MenuKeysActivos = activos };
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                "Central",
                "ModuloMenuFiltroCheck",
                ex,
                "No se pudo calcular el filtro de menú por módulos; se muestra el menú completo.",
                new { idCliente },
                AppEventSeverity.Warning,
                ct).ConfigureAwait(false);
            return null;
        }
    }

    private static async Task<IReadOnlyList<ModuloDto>> QueryModulosAsync(SqlConnection cn, CancellationToken ct)
    {
        const string modulosSql = """
            SELECT Id, Codigo, Nombre, Descripcion, ISNULL(MenuKeyRaiz, '') AS MenuKeyRaiz, Precio, Activo
            FROM dbo.Modulos
            ORDER BY Nombre;
            """;
        const string depsSql = """
            SELECT d.IdModulo, d.IdModuloDependeDe, m.Codigo, m.Nombre, d.EsObligatoria
            FROM dbo.ModulosDependencias d
            INNER JOIN dbo.Modulos m ON m.Id = d.IdModuloDependeDe;
            """;

        var modulos = (await cn.QueryAsync<ModuloRow>(new CommandDefinition(modulosSql, cancellationToken: ct)).ConfigureAwait(false)).ToList();
        var deps = (await cn.QueryAsync<DependenciaRow>(new CommandDefinition(depsSql, cancellationToken: ct)).ConfigureAwait(false)).ToLookup(x => x.IdModulo);

        return modulos.Select(m => new ModuloDto
        {
            Id = m.Id,
            Codigo = m.Codigo,
            Nombre = m.Nombre,
            Descripcion = m.Descripcion,
            MenuKeyRaiz = m.MenuKeyRaiz,
            Precio = m.Precio,
            Activo = m.Activo,
            Dependencias = deps[m.Id].Select(x => new ModuloDependenciaDto
            {
                IdModuloDependeDe = x.IdModuloDependeDe,
                Codigo = x.Codigo,
                Nombre = x.Nombre,
                EsObligatoria = x.EsObligatoria
            }).ToList()
        }).ToArray();
    }

    private static IReadOnlyCollection<int> ResolveDependenciasTransitivas(IReadOnlyCollection<ModuloDto> catalogo, int idModulo)
    {
        var porId = catalogo.ToDictionary(m => m.Id);
        var resultado = new HashSet<int>();
        var pendientes = new Queue<int>();
        pendientes.Enqueue(idModulo);

        while (pendientes.Count > 0)
        {
            var actual = pendientes.Dequeue();
            if (!resultado.Add(actual) || !porId.TryGetValue(actual, out var moduloActual))
                continue;

            // Las opcionales (EsObligatoria = false) NO se activan solas: son solo un enganche
            // informativo que las pantallas usan para decidir si mostrar una función puntual.
            foreach (var dependencia in moduloActual.Dependencias.Where(d => d.EsObligatoria))
            {
                if (!resultado.Contains(dependencia.IdModuloDependeDe))
                    pendientes.Enqueue(dependencia.IdModuloDependeDe);
            }
        }

        return resultado;
    }

    private static async Task ReplaceDependenciasAsync(SqlConnection cn, SqlTransaction tx, int idModulo, IReadOnlyCollection<DependenciaSeleccionDto> dependencias, CancellationToken ct)
    {
        const string deleteSql = "DELETE FROM dbo.ModulosDependencias WHERE IdModulo = @IdModulo;";
        await cn.ExecuteAsync(new CommandDefinition(deleteSql, new { IdModulo = idModulo }, tx, cancellationToken: ct)).ConfigureAwait(false);

        var distinctDeps = dependencias
            .Where(d => d.IdModulo != idModulo)
            .GroupBy(d => d.IdModulo)
            .Select(g => g.First())
            .ToArray();
        if (distinctDeps.Length == 0)
            return;

        const string insertSql = """
            INSERT INTO dbo.ModulosDependencias (IdModulo, IdModuloDependeDe, EsObligatoria)
            VALUES (@IdModulo, @IdModuloDependeDe, @EsObligatoria);
            """;

        foreach (var dependencia in distinctDeps)
        {
            await cn.ExecuteAsync(new CommandDefinition(insertSql, new
            {
                IdModulo = idModulo,
                IdModuloDependeDe = dependencia.IdModulo,
                dependencia.EsObligatoria
            }, tx, cancellationToken: ct)).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ExistsModuloCodigoAsync(SqlConnection cn, string codigo, int? excludeId, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1) FROM dbo.Modulos
            WHERE UPPER(LTRIM(RTRIM(Codigo))) = UPPER(LTRIM(RTRIM(@Codigo)))
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
            """;
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Codigo = codigo, ExcludeId = excludeId }, cancellationToken: ct)).ConfigureAwait(false);
        return count > 0;
    }

    private static async Task<bool> IsClienteLegacyAsync(SqlConnection cn, string idCliente, CancellationToken ct)
    {
        const string sql = """
            SELECT ISNULL(EsClienteLegacy, 0)
            FROM dbo.Clientes
            WHERE UPPER(LTRIM(RTRIM(idcliente))) = UPPER(LTRIM(RTRIM(@IdCliente)));
            """;
        var result = await cn.ExecuteScalarAsync<bool?>(new CommandDefinition(sql, new { IdCliente = idCliente }, cancellationToken: ct)).ConfigureAwait(false);
        return result ?? false;
    }

    private sealed class ModuloRow
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string MenuKeyRaiz { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public bool Activo { get; set; }
    }

    private sealed class DependenciaRow
    {
        public int IdModulo { get; set; }
        public int IdModuloDependeDe { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public bool EsObligatoria { get; set; }
    }

    private sealed class ClienteModuloRow
    {
        public int IdModulo { get; set; }
        public string Estado { get; set; } = string.Empty;
        public DateTime ActivadoUtc { get; set; }
        public string? ActivadoPor { get; set; }
        public DateTime? SolicitadoUtc { get; set; }
        public string? SolicitadoPor { get; set; }
        public DateTime? PruebaVenceUtc { get; set; }
    }

    /// <summary>
    /// Un módulo cuenta como activo si está en <c>Activo</c>, o en <c>Prueba</c> con una fecha
    /// de vencimiento todavía no cumplida (o sin fecha cargada, defensivo). Único lugar con esta
    /// regla — todos los chequeos de "¿está prendido?" deben pasar por acá.
    /// </summary>
    private static bool EstaEnPruebaVigente(string? estado, DateTime? pruebaVenceUtc)
        => string.Equals(estado, ClienteModuloEstados.Prueba, StringComparison.OrdinalIgnoreCase)
           && (pruebaVenceUtc is null || pruebaVenceUtc.Value > DateTime.UtcNow);

    private async Task<AdminClienteDto?> QueryClienteAsync(string filterSql, object parameters, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS RazonSocial,
                ISNULL(idweb, '') AS IdWeb,
                ISNULL(superadmin, 0) AS SuperAdmin
            FROM dbo.Clientes
            {filterSql};
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await cn.QuerySingleOrDefaultAsync<AdminClienteDto>(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<AdminBaseDto?> QueryBaseAsync(string filterSql, object parameters, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                id AS IdBase,
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS Nombre,
                ISNULL(dbserver, '') AS DbServer,
                ISNULL(dbname, '') AS DbName,
                ISNULL(dbuser, '') AS DbUser,
                ISNULL(dbpassword, '') AS DbPassword
            FROM dbo.bases
            {filterSql};
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await cn.QuerySingleOrDefaultAsync<AdminBaseDto>(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<AdminUserDto?> QueryUserAsync(string filterSql, object parameters, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                u.[user] AS UserName,
                u.idcliente AS IdCliente,
                ISNULL(c.nombre, '') AS RazonSocial,
                ISNULL(u.password, '') AS Password
            FROM dbo.users u
            LEFT JOIN dbo.Clientes c ON c.idcliente = u.idcliente
            {filterSql};
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await cn.QuerySingleOrDefaultAsync<AdminUserDto>(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<bool> ExistsClienteAsync(string idCliente, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        return await ExistsClienteAsync(cn, idCliente, ct).ConfigureAwait(false);
    }

    private static async Task<bool> ExistsClienteAsync(SqlConnection cn, string idCliente, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(1) FROM dbo.Clientes WHERE UPPER(LTRIM(RTRIM(idcliente))) = UPPER(LTRIM(RTRIM(@IdCliente)));";
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { IdCliente = idCliente }, cancellationToken: ct)).ConfigureAwait(false);
        return count > 0;
    }

    private static async Task<bool> ExistsClienteByWebAsync(SqlConnection cn, string idWeb, CancellationToken ct, string? excludeIdCliente = null)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Clientes
            WHERE UPPER(LTRIM(RTRIM(ISNULL(idweb, '')))) = UPPER(LTRIM(RTRIM(@IdWeb)))
              AND (@ExcludeIdCliente IS NULL OR UPPER(LTRIM(RTRIM(idcliente))) <> UPPER(LTRIM(RTRIM(@ExcludeIdCliente))));
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            IdWeb = idWeb,
            ExcludeIdCliente = excludeIdCliente
        }, cancellationToken: ct)).ConfigureAwait(false);
        return count > 0;
    }

    private static async Task<bool> ExistsUserAsync(SqlConnection cn, string userName, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(1) FROM dbo.users WHERE UPPER(LTRIM(RTRIM([user]))) = UPPER(LTRIM(RTRIM(@UserName)));";
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { UserName = userName }, cancellationToken: ct)).ConfigureAwait(false);
        return count > 0;
    }

    private static async Task<string> GetUserPasswordAsync(SqlConnection cn, string userName, CancellationToken ct)
    {
        const string sql = "SELECT TOP (1) ISNULL(password, '') FROM dbo.users WHERE UPPER(LTRIM(RTRIM([user]))) = UPPER(LTRIM(RTRIM(@UserName)));";
        return await cn.ExecuteScalarAsync<string>(new CommandDefinition(sql, new { UserName = userName }, cancellationToken: ct)).ConfigureAwait(false) ?? string.Empty;
    }

    private static async Task<string> GetBasePasswordAsync(SqlConnection cn, int idBase, CancellationToken ct)
    {
        const string sql = "SELECT TOP (1) ISNULL(dbpassword, '') FROM dbo.bases WHERE id = @IdBase;";
        return await cn.ExecuteScalarAsync<string>(new CommandDefinition(sql, new { IdBase = idBase }, cancellationToken: ct)).ConfigureAwait(false) ?? string.Empty;
    }

    private static async Task UpsertInitialUserAsync(SqlConnection cn, SqlTransaction tx, string userName, string idCliente, string password, CancellationToken ct)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.users WHERE UPPER(LTRIM(RTRIM([user]))) = UPPER(LTRIM(RTRIM(@UserName))))
            BEGIN
                UPDATE dbo.users
                SET password = @Password,
                    idcliente = @IdCliente
                WHERE UPPER(LTRIM(RTRIM([user]))) = UPPER(LTRIM(RTRIM(@UserName)));
            END
            ELSE
            BEGIN
                INSERT INTO dbo.users ([user], password, idcliente)
                VALUES (@UserName, @Password, @IdCliente);
            END
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserName = userName,
            Password = password,
            IdCliente = idCliente
        }, tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static string NormalizeKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeHostWeb(string? value)
    {
        var text = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        var previousDash = false;
        foreach (var ch in text.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            var lower = char.ToLowerInvariant(ch);
            if (char.IsLetterOrDigit(lower))
            {
                builder.Append(lower);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private sealed class ClientInitialPasswordRow
    {
        public string? PasswordInicial { get; set; }
    }
}
