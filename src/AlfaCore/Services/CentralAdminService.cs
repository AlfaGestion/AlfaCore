using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CentralAdminService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : ICentralAdminService
{
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
