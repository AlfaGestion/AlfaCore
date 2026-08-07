using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface ICentralProvisioningService
{
    Task<PublicProvisioningResult> ProvisionAsync(PublicProvisioningRequest request, CancellationToken ct = default);
}

public sealed class CentralProvisioningService(
    IConfiguration configuration,
    IWebHostEnvironment env,
    IAppEventService appEvents,
    UsuariosPasswordCodec passwordCodec) : ICentralProvisioningService
{
    private const string ModuleName = "RegistroPublico";
    private const string LegacySystemCode = "CN000PR";
    private const string DefaultCaja = "1";
    private const string DefaultUnidadNegocio = "   1";
    private const string ConfigGroup = "SISTEMA";
    private const string FechaUpdateKey = "FECHAUPDATE_CORE";
    private const string DefaultLoginLanguage = "Español";
    private const string StatsDatabaseName = "NW_ESTADISTICAS";

    private string CentralConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    private string AlfaGestionConnectionString => configuration.GetConnectionString("AlfaGestion")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    private string UpdatesPath => Path.Combine(env.ContentRootPath, "App_Data", "updates");

    public async Task<PublicProvisioningResult> ProvisionAsync(PublicProvisioningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dbName = BuildDatabaseName(request.IdCliente);
        var credentials = ResolveDatabaseCredentials(dbName);
        var targetServer = ResolveTargetServer();
        var templatePath = configuration["RegistroPublico:BackupTemplatePath"]?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(templatePath))
            throw new InvalidOperationException("Falta configurar RegistroPublico:BackupTemplatePath en el servidor.");

        try
        {
            if (await BaseAlreadyRegisteredAsync(request.IdCliente, dbName, ct))
            {
                // Puede pasar que el mismo CUIT se registre de nuevo con OTRO email (p. ej. otro
                // usuario de la misma empresa) — sp_web_altaClienteAlfa reutiliza el mismo
                // idCliente en ese caso. La base ya existe y no hay que restaurarla de nuevo, pero
                // si no se crea el usuario legacy (TA_USUARIOS) para este email puntual, el login
                // automático por email (ResolveByCentralLoginAsync) nunca lo va a encontrar y ese
                // usuario queda forzado a tipear el "usuario del sistema" a mano para siempre.
                var existingTargetConnectionString = BuildDatabaseConnectionString(targetServer, dbName, credentials.User, credentials.Password);
                await using var existingTarget = new SqlConnection(existingTargetConnectionString);
                await existingTarget.OpenAsync(ct);
                await EnsureLegacyUserAsync(existingTarget, request, ct);

                return new PublicProvisioningResult
                {
                    Success = true,
                    DatabaseName = dbName,
                    DatabaseUser = credentials.User,
                    Message = "La base ya estaba aprovisionada."
                };
            }

            var masterConnectionString = BuildMasterConnectionString(targetServer);
            await using var master = new SqlConnection(masterConnectionString);
            await master.OpenAsync(ct);

            if (!await DatabaseExistsAsync(master, dbName, ct))
            {
                var fileList = await LoadBackupFileListAsync(master, templatePath, ct);
                if (fileList.Count == 0)
                    throw new InvalidOperationException("No se pudieron leer los archivos lógicos del backup plantilla.");

                var defaultPaths = await LoadDefaultDatabasePathsAsync(master, ct);
                await RestoreDatabaseAsync(master, dbName, templatePath, fileList, defaultPaths, ct);
            }

            await EnsureSqlLoginAsync(master, credentials, dbName, ct);

            master.ChangeDatabase(dbName);
            await EnsureDatabaseUserAsync(master, credentials.User, ct);
            master.ChangeDatabase("master");

            await EnsureStatsDatabaseAccessAsync(master, targetServer, credentials.User, ct);

            var targetConnectionString = BuildDatabaseConnectionString(targetServer, dbName, credentials.User, credentials.Password);
            await using var target = new SqlConnection(targetConnectionString);
            await target.OpenAsync(ct);

            await ApplyPendingUpdatesAsync(target, ct);
            await UpdateCoreConfigurationAsync(target, request, ct);
            await EnsureLegacyUserAsync(target, request, ct);

            await RegisterBaseAsync(request, targetServer, dbName, credentials, ct);

            await appEvents.LogAuditAsync(
                ModuleName,
                "Provision",
                "ALFA_CENTRAL.bases",
                $"{request.IdCliente}|{dbName}",
                "Base aprovisionada a partir de la plantilla de backup.",
                new
                {
                    request.IdCliente,
                    request.RazonSocial,
                    DbServer = targetServer,
                    DbName = dbName,
                    DbUser = credentials.User
                },
                ct);

            return new PublicProvisioningResult
            {
                Success = true,
                DatabaseName = dbName,
                DatabaseUser = credentials.User,
                Message = "La base se aprovisionó correctamente."
            };
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                ModuleName,
                "Provision",
                ex,
                "No se pudo aprovisionar la base del registro público.",
                new
                {
                    request.IdCliente,
                    request.RazonSocial,
                    DbName = dbName,
                    DbServer = targetServer
                },
                AppEventSeverity.Error,
                ct);
            throw;
        }
    }

    private string ResolveTargetServer()
    {
        var configured = configuration["RegistroPublico:SqlServer"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var builder = new SqlConnectionStringBuilder(AlfaGestionConnectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException("No se pudo resolver el servidor SQL objetivo para el aprovisionamiento.");

        return builder.DataSource;
    }

    private string BuildMasterConnectionString(string targetServer)
    {
        var source = new SqlConnectionStringBuilder(AlfaGestionConnectionString)
        {
            DataSource = targetServer,
            InitialCatalog = "master",
            ApplicationName = "AlfaCore-RegistroPublico"
        };

        return source.ConnectionString;
    }

    private static string BuildDatabaseConnectionString(string server, string dbName, string user, string password)
        => new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = dbName,
            IntegratedSecurity = false,
            UserID = user,
            Password = password,
            TrustServerCertificate = true,
            ApplicationName = "AlfaCore-RegistroPublico"
        }.ConnectionString;

    private static (string User, string Password) ResolveDatabaseCredentials(string dbName)
        => (dbName, dbName);

    private static string BuildDatabaseName(string idCliente)
    {
        var normalized = new string((idCliente ?? string.Empty).Trim().Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("No se pudo construir el nombre de la base porque el código de cliente es inválido.");

        return $"AW_{normalized.ToUpperInvariant()}";
    }

    private async Task<bool> BaseAlreadyRegisteredAsync(string idCliente, string dbName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.bases
            WHERE UPPER(LTRIM(RTRIM(idcliente))) = UPPER(LTRIM(RTRIM(@IdCliente)))
              AND UPPER(LTRIM(RTRIM(dbname))) = UPPER(LTRIM(RTRIM(@DbName)));
            """;

        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { IdCliente = idCliente, DbName = dbName }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<bool> DatabaseExistsAsync(SqlConnection master, string dbName, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(1) FROM sys.databases WHERE name = @DbName;";
        var count = await master.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { DbName = dbName }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<IReadOnlyList<BackupFileRow>> LoadBackupFileListAsync(SqlConnection master, string templatePath, CancellationToken ct)
    {
        var sql = $"RESTORE FILELISTONLY FROM DISK = N'{EscapeSqlLiteral(templatePath)}';";
        var rows = await master.QueryAsync<BackupFileRow>(new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 1200));
        return rows.ToArray();
    }

    private static async Task<(string DataPath, string LogPath)> LoadDefaultDatabasePathsAsync(SqlConnection master, CancellationToken ct)
    {
        const string sql = """
            SELECT
                CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath')) AS DataPath,
                CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultLogPath')) AS LogPath;
            """;

        var row = await master.QuerySingleAsync<DefaultPathRow>(new CommandDefinition(sql, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(row.DataPath) || string.IsNullOrWhiteSpace(row.LogPath))
            throw new InvalidOperationException("No se pudieron resolver las rutas por defecto del servidor SQL para restaurar la base.");

        return (row.DataPath.Trim(), row.LogPath.Trim());
    }

    private static async Task RestoreDatabaseAsync(
        SqlConnection master,
        string dbName,
        string templatePath,
        IReadOnlyList<BackupFileRow> fileList,
        (string DataPath, string LogPath) defaultPaths,
        CancellationToken ct)
    {
        var moves = new StringBuilder();
        var index = 0;
        foreach (var file in fileList)
        {
            var extension = string.Equals(file.Type, "L", StringComparison.OrdinalIgnoreCase) ? ".ldf" : ".mdf";
            var baseName = index == 0 ? dbName : $"{dbName}_{index}";
            var targetFolder = string.Equals(file.Type, "L", StringComparison.OrdinalIgnoreCase) ? defaultPaths.LogPath : defaultPaths.DataPath;
            var targetPath = Path.Combine(targetFolder, baseName + extension);

            if (moves.Length > 0)
                moves.AppendLine(",");

            moves.Append($"MOVE N'{EscapeSqlLiteral(file.LogicalName)}' TO N'{EscapeSqlLiteral(targetPath)}'");
            index++;
        }

        var sql = $"""
            RESTORE DATABASE [{dbName}]
            FROM DISK = N'{EscapeSqlLiteral(templatePath)}'
            WITH RECOVERY,
                 REPLACE,
                 {moves};
            """;

        await master.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 3600));
    }

    private static async Task EnsureSqlLoginAsync(SqlConnection master, (string User, string Password) credentials, string dbName, CancellationToken ct)
    {
        var sql = $"""
            IF EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'{EscapeSqlLiteral(credentials.User)}')
            BEGIN
                ALTER LOGIN [{credentials.User}] WITH PASSWORD = N'{EscapeSqlLiteral(credentials.Password)}', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{dbName}], DEFAULT_LANGUAGE = [{DefaultLoginLanguage}];
            END
            ELSE
            BEGIN
                CREATE LOGIN [{credentials.User}] WITH PASSWORD = N'{EscapeSqlLiteral(credentials.Password)}', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{dbName}], DEFAULT_LANGUAGE = [{DefaultLoginLanguage}];
            END
            """;

        await master.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private async Task EnsureStatsDatabaseAccessAsync(SqlConnection master, string targetServer, string loginName, CancellationToken ct)
    {
        if (!await DatabaseExistsAsync(master, StatsDatabaseName, ct))
            return;

        var statsConnectionString = new SqlConnectionStringBuilder(AlfaGestionConnectionString)
        {
            DataSource = targetServer,
            InitialCatalog = StatsDatabaseName,
            ApplicationName = "AlfaCore-RegistroPublico"
        }.ConnectionString;

        await using var stats = new SqlConnection(statsConnectionString);
        await stats.OpenAsync(ct);

        var sql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{EscapeSqlLiteral(loginName)}')
            BEGIN
                CREATE USER [{loginName}] FOR LOGIN [{loginName}];
            END

            IF NOT EXISTS (
                SELECT 1
                FROM sys.database_role_members rm
                INNER JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
                INNER JOIN sys.database_principals u ON u.principal_id = rm.member_principal_id
                WHERE r.name = N'db_datareader'
                  AND u.name = N'{EscapeSqlLiteral(loginName)}')
            BEGIN
                ALTER ROLE [db_datareader] ADD MEMBER [{loginName}];
            END
            """;

        await stats.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private static async Task EnsureDatabaseUserAsync(SqlConnection target, string loginName, CancellationToken ct)
    {
        var sql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{EscapeSqlLiteral(loginName)}')
            BEGIN
                CREATE USER [{loginName}] FOR LOGIN [{loginName}];
            END

            IF NOT EXISTS (
                SELECT 1
                FROM sys.database_role_members rm
                INNER JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
                INNER JOIN sys.database_principals u ON u.principal_id = rm.member_principal_id
                WHERE r.name = N'db_owner'
                  AND u.name = N'{EscapeSqlLiteral(loginName)}')
            BEGIN
                ALTER ROLE [db_owner] ADD MEMBER [{loginName}];
            END
            """;

        await target.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private static async Task UpdateCoreConfigurationAsync(SqlConnection target, PublicProvisioningRequest request, CancellationToken ct)
    {
        if (!await TableExistsAsync(target, "TA_CONFIGURACION", ct))
            throw new InvalidOperationException("La plantilla restaurada no tiene TA_CONFIGURACION.");

        var detailColumn = await ResolveConfigDetailColumnAsync(target, ct);
        await SaveConfigValueAsync(target, detailColumn, "NOMBRE", request.RazonSocial, ct);
        await SaveConfigValueAsync(target, detailColumn, "TELEFONO", request.Telefono, ct);
        await SaveConfigValueAsync(target, detailColumn, "EMAIL_DE", request.Email, ct);
        await SaveConfigValueAsync(target, detailColumn, "CUIT", request.Cuit, ct);
        await SaveConfigValueAsync(target, detailColumn, "CONDIVA", request.Iva, ct);
    }

    private async Task EnsureLegacyUserAsync(SqlConnection target, PublicProvisioningRequest request, CancellationToken ct)
    {
        if (!await TableExistsAsync(target, "TA_USUARIOS", ct))
            throw new InvalidOperationException("La plantilla restaurada no tiene TA_USUARIOS.");

        var hasActivo = await ColumnExistsAsync(target, "TA_USUARIOS", "Activo", ct);
        var encodedPassword = passwordCodec.Encode(request.Password.Trim());

        var sql = hasActivo
            ? """
              IF EXISTS (
                  SELECT 1
                  FROM dbo.TA_USUARIOS
                  WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                    AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Nombre)
              BEGIN
                  UPDATE dbo.TA_USUARIOS
                  SET
                      PASSWORD = @Password,
                      email_de = @Email,
                      EsGrupo = 0,
                      CambiarProximoInicio = 0,
                      IDCAJA = @IdCaja,
                      UNEGOCIO = @UNegocio,
                      V_ModificaArtLuegoDeCargado = 1,
                      Activo = 1,
                      FechaHora_Modificacion = GETDATE()
                  WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                    AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Nombre;
              END
              ELSE
              BEGIN
                  INSERT INTO dbo.TA_USUARIOS
                  (
                      Nombre,
                      Sistema,
                      Password,
                      FechaHora_Grabacion,
                      FechaHora_Modificacion,
                      CambiarProximoInicio,
                      EsGrupo,
                      IDCAJA,
                      UNEGOCIO,
                      email_de,
                      V_ModificaArtLuegoDeCargado,
                      Activo
                  )
                  VALUES
                  (
                      @NombreRaw,
                      @SistemaRaw,
                      @Password,
                      GETDATE(),
                      GETDATE(),
                      0,
                      0,
                      @IdCaja,
                      @UNegocio,
                      @Email,
                      1,
                      1
                  );
              END
              """
            : """
              IF EXISTS (
                  SELECT 1
                  FROM dbo.TA_USUARIOS
                  WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                    AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Nombre)
              BEGIN
                  UPDATE dbo.TA_USUARIOS
                  SET
                      PASSWORD = @Password,
                      email_de = @Email,
                      EsGrupo = 0,
                      CambiarProximoInicio = 0,
                      IDCAJA = @IdCaja,
                      UNEGOCIO = @UNegocio,
                      V_ModificaArtLuegoDeCargado = 1,
                      FechaHora_Modificacion = GETDATE()
                  WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                    AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Nombre;
              END
              ELSE
              BEGIN
                  INSERT INTO dbo.TA_USUARIOS
                  (
                      Nombre,
                      Sistema,
                      Password,
                      FechaHora_Grabacion,
                      FechaHora_Modificacion,
                      CambiarProximoInicio,
                      EsGrupo,
                      IDCAJA,
                      UNEGOCIO,
                      email_de,
                      V_ModificaArtLuegoDeCargado
                  )
                  VALUES
                  (
                      @NombreRaw,
                      @SistemaRaw,
                      @Password,
                      GETDATE(),
                      GETDATE(),
                      0,
                      0,
                      @IdCaja,
                      @UNegocio,
                      @Email,
                      1
                  );
              END
              """;

        await target.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Nombre = request.UserName.Trim().ToUpperInvariant(),
                NombreRaw = request.UserName.Trim(),
                Sistema = LegacySystemCode.ToUpperInvariant(),
                SistemaRaw = LegacySystemCode,
                Password = encodedPassword,
                Email = request.Email.Trim(),
                IdCaja = DefaultCaja,
                UNegocio = DefaultUnidadNegocio
            },
            cancellationToken: ct));
    }

    private async Task ApplyPendingUpdatesAsync(SqlConnection target, CancellationToken ct)
    {
        if (!Directory.Exists(UpdatesPath))
            return;

        var detailColumn = await ResolveConfigDetailColumnAsync(target, ct);
        var currentVersionText = await GetConfigValueAsync(target, detailColumn, FechaUpdateKey, ct);
        var currentVersion = ParseVersionToken(currentVersionText);
        var scripts = GetUpdateScripts();
        var pending = currentVersion is not null
            ? scripts.Where(x => CompareScriptToVersion(x, currentVersion) > 0).ToList()
            : scripts;

        foreach (var script in pending)
        {
            var sql = await File.ReadAllTextAsync(script.FullPath, ct);
            foreach (var batch in SplitSqlBatches(sql))
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;

                await target.ExecuteAsync(new CommandDefinition(batch, cancellationToken: ct, commandTimeout: 1200));
            }

            await SaveConfigValueAsync(target, detailColumn, FechaUpdateKey, script.VersionKey, ct);
        }
    }

    private List<UpdateScript> GetUpdateScripts()
    {
        var culture = CultureInfo.InvariantCulture;
        return Directory.GetFiles(UpdatesPath, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Select(file => new
            {
                File = file,
                Match = Regex.Match(file.Name, @"^(?<date>\d{4}-\d{2}-\d{2})-(?<seq>\d{3})")
            })
            .Where(x => x.Match.Success)
            .Select(x => new UpdateScript(
                x.File.Name,
                x.File.FullName,
                $"{x.Match.Groups["date"].Value}-{x.Match.Groups["seq"].Value}",
                DateOnly.ParseExact(x.Match.Groups["date"].Value, "yyyy-MM-dd", culture),
                int.Parse(x.Match.Groups["seq"].Value, culture)))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Sequence)
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task RegisterBaseAsync(PublicProvisioningRequest request, string server, string dbName, (string User, string Password) credentials, CancellationToken ct)
    {
        await using var central = new SqlConnection(CentralConnectionString);
        await central.OpenAsync(ct);

        var columns = await GetColumnNamesAsync(central, "bases", ct);
        var names = new List<string> { "idcliente", "nombre", "dbserver", "dbname", "dbuser", "dbpassword" };
        var values = new List<string> { "@IdCliente", "@Nombre", "@DbServer", "@DbName", "@DbUser", "@DbPassword" };

        if (columns.Contains("path"))
        {
            names.Add("path");
            values.Add("@Path");
        }

        var sql = $"""
            INSERT INTO dbo.bases ({string.Join(", ", names)})
            VALUES ({string.Join(", ", values)});
            """;

        await central.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                IdCliente = request.IdCliente.Trim(),
                Nombre = request.RazonSocial.Trim(),
                DbServer = server,
                DbName = dbName,
                DbUser = credentials.User,
                DbPassword = credentials.Password,
                Path = "."
            },
            cancellationToken: ct));
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT LOWER(name)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@ObjectName);
            """;

        var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new { ObjectName = $"dbo.{tableName}" }, cancellationToken: ct));
        return rows.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE WHEN OBJECT_ID(@ObjectName, 'U') IS NULL THEN 0 ELSE 1 END;
            """;

        var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { ObjectName = $"dbo.{tableName}" }, cancellationToken: ct));
        return exists == 1;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@ObjectName, 'U')
              AND LOWER(name) = LOWER(@ColumnName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new
            {
                ObjectName = $"dbo.{tableName}",
                ColumnName = columnName
            },
            cancellationToken: ct));
        return count > 0;
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name;
            """;

        var result = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(result) ? "DESCRIPCION" : result.Trim();
    }

    private static async Task<string> GetConfigValueAsync(SqlConnection cn, string detailColumn, string key, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", key.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        var value = rd.IsDBNull(0) ? string.Empty : Convert.ToString(rd.GetValue(0)) ?? string.Empty;
        var aux = rd.IsDBNull(1) ? string.Empty : Convert.ToString(rd.GetValue(1)) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value) ? value.Trim() : aux.Trim();
    }

    private static async Task SaveConfigValueAsync(SqlConnection cn, string detailColumn, string key, string value, CancellationToken ct)
    {
        var stored = SplitStoredValue(value);
        var sql = $"""
            UPDATE dbo.TA_CONFIGURACION
            SET
                VALOR = @Valor,
                {detailColumn} = @ValorAux,
                GRUPO = @Grupo
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                VALUES (@Clave, @Valor, @ValorAux, @Grupo);
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ClaveNormalizada", key.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Clave", key);
        cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
        cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
        cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150
            ? (string.Empty, normalized)
            : (normalized, string.Empty);
    }

    private static object DbNullable(string value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static IEnumerable<string> SplitSqlBatches(string sql)
    {
        return Regex.Split(sql, @"^\s*GO\s*($|\-\-.*$)", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch));
    }

    private static ScriptVersion? ParseVersionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Regex.Match(value.Trim(), @"^(?<date>\d{4}-\d{2}-\d{2})-(?<seq>\d{3})$");
        if (!match.Success)
            return null;

        return new ScriptVersion(
            DateOnly.ParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            int.Parse(match.Groups["seq"].Value, CultureInfo.InvariantCulture));
    }

    private static int CompareScriptToVersion(UpdateScript script, ScriptVersion currentVersion)
    {
        var dateComparison = script.Date.CompareTo(currentVersion.Date);
        return dateComparison != 0 ? dateComparison : script.Sequence.CompareTo(currentVersion.Sequence);
    }

    private static string EscapeSqlLiteral(string value)
        => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

    private sealed record UpdateScript(string FileName, string FullPath, string VersionKey, DateOnly Date, int Sequence);
    private sealed record ScriptVersion(DateOnly Date, int Sequence);

    private sealed class BackupFileRow
    {
        public string LogicalName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class DefaultPathRow
    {
        public string DataPath { get; set; } = string.Empty;
        public string LogPath { get; set; } = string.Empty;
    }
}
