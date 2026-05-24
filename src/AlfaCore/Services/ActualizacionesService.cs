using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class ActualizacionesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IWebHostEnvironment env,
    IAppEventService appEvents) : IActualizacionesService
{
    private const string ConfigGroup = "SISTEMA";
    private const string FechaUpdateKey = "FECHAUPDATE_CORE";
    private const string RutaRedKey = "ACTUALIZACIONESRUTARED";
    private const string HistoryTable = "dbo.AUX_ACTUALIZACION_HIST";
    private const string LockResourcePrefix = "ALFACORE_DB_UPDATES";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    private string RutaLocalUpdates => Path.Combine(env.ContentRootPath, "App_Data", "updates");

    public Task<ActualizacionesSettingsDto> GetSettingsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Actualizaciones", "GetSettings", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            var rutaRed = await GetConfigValueAsync(cn, detailColumn, RutaRedKey, token);
            return new ActualizacionesSettingsDto
            {
                RutaRed = rutaRed
            };
        }, "No se pudo cargar la configuración de actualizaciones.", ct);

    public Task SaveSettingsAsync(ActualizacionesSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync("Actualizaciones", "SaveSettings", async token =>
        {
            ArgumentNullException.ThrowIfNull(settings);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await UpsertConfigValueAsync(cn, detailColumn, RutaRedKey, NormalizePath(settings.RutaRed), token);

            await appEvents.LogAuditAsync(
                "Actualizaciones",
                "SaveSettings",
                "TA_CONFIGURACION",
                RutaRedKey,
                "Configuración de actualizaciones actualizada.",
                new { RutaRed = NormalizePath(settings.RutaRed) },
                token);

            return true;
        }, "No se pudo guardar la ruta de actualizaciones.", ct);

    public Task<ActualizacionesDashboardDto> GetDashboardAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Actualizaciones", "GetDashboard", async token =>
        {
            var source = ResolveSource(await GetSettingsAsync(token), validateAvailability: false);
            var scripts = GetScripts(source, requireAvailability: false);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await EnsureHistoryTableAsync(cn, token);

            var fechaUpdate = await GetConfigValueAsync(cn, detailColumn, FechaUpdateKey, token);
            var currentVersion = ParseVersionToken(fechaUpdate);
            var historial = await GetHistoryAsync(cn, token);

            return new ActualizacionesDashboardDto
            {
                FechaUpdateActual = NormalizeVersionText(currentVersion),
                RutaLocal = source.RutaLocal,
                RutaRed = source.RutaRed,
                RutaOrigenActiva = source.RutaOrigenActiva,
                RutaRedDisponible = source.RutaRedDisponible,
                UsaRutaRed = source.UsaRutaRed,
                Scripts = scripts,
                Pendientes = currentVersion.HasValue
                    ? scripts.Where(x => CompareScriptToVersion(x, currentVersion.Value) > 0).ToArray()
                    : scripts,
                Historial = historial
            };
        }, "No se pudo cargar el panel de actualizaciones.", ct);

    public Task<ActualizacionesRunResultDto> ExecutePendingAsync(ActualizacionesRunRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Actualizaciones", "ExecutePending", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await EnsureHistoryTableAsync(cn, token);

            var settings = await GetSettingsAsync(token);
            var source = ResolveSource(settings, validateAvailability: !request.ForzarRutaLocal, request.ForzarRutaLocal);
            var lockHandle = await AcquireLockAsync(cn, token);

            try
            {
                var scripts = GetScripts(source, requireAvailability: true);
                var currentVersionText = await GetConfigValueAsync(cn, detailColumn, FechaUpdateKey, token);
                var currentVersion = ParseVersionToken(currentVersionText);
                var pending = currentVersion.HasValue
                    ? scripts.Where(x => CompareScriptToVersion(x, currentVersion.Value) > 0).ToList()
                    : scripts.ToList();

                if (pending.Count == 0)
                {
                    return new ActualizacionesRunResultDto
                    {
                        CantidadAplicada = 0,
                        VersionAnterior = NormalizeVersionText(currentVersion),
                        VersionFinal = NormalizeVersionText(currentVersion),
                        RutaOrigen = source.RutaOrigenActiva,
                        SinCambios = true
                    };
                }

                var applied = new List<string>();
                var versionAnterior = NormalizeVersionText(currentVersion);
                var actor = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
                var pc = NormalizeActor(request.PcAccion, Environment.MachineName, 100);

                foreach (var script in pending)
                {
                    try
                    {
                        var sql = await File.ReadAllTextAsync(script.RutaCompleta, token);
                        foreach (var batch in SplitSqlBatches(sql))
                        {
                            if (string.IsNullOrWhiteSpace(batch))
                                continue;

                            await using var cmd = new SqlCommand(batch, cn)
                            {
                                CommandTimeout = 1200
                            };
                            await cmd.ExecuteNonQueryAsync(token);
                        }

                        await UpsertConfigValueAsync(cn, detailColumn, FechaUpdateKey, script.VersionKey, token);
                        await InsertHistoryAsync(cn, script, versionAnterior, script.VersionKey, source.RutaOrigenActiva, "OK",
                            "Actualización aplicada correctamente.", actor, pc, null, token);

                        await appEvents.LogAuditAsync(
                            "Actualizaciones",
                            "ApplyScript",
                            HistoryTable,
                            script.Archivo,
                            "Actualización SQL aplicada.",
                            new
                            {
                                script.Archivo,
                                VersionAnterior = versionAnterior,
                                VersionNueva = script.VersionKey,
                                RutaOrigen = source.RutaOrigenActiva
                            },
                            token);

                        versionAnterior = script.VersionKey;
                        applied.Add(script.Archivo);
                    }
                    catch (Exception ex)
                    {
                        await InsertHistoryAsync(cn, script, versionAnterior, script.VersionKey, source.RutaOrigenActiva, "ERROR",
                            "La actualización falló y quedó registrada para revisión.", actor, pc, ex.ToString(), token);

                        var incidentId = await appEvents.LogErrorAsync(
                            "Actualizaciones",
                            "ApplyScript",
                            ex,
                            $"No se pudo aplicar la actualización {script.Archivo}.",
                            new
                            {
                                script.Archivo,
                                script.RutaCompleta,
                                VersionAnterior = versionAnterior,
                                VersionObjetivo = script.VersionKey
                            },
                            AppEventSeverity.Error,
                            token);

                        throw new AppUserFacingException(
                            $"No se pudo aplicar la actualización {script.Archivo}. Revisá el historial del módulo.",
                            incidentId,
                            ex);
                    }
                }

                return new ActualizacionesRunResultDto
                {
                    CantidadAplicada = applied.Count,
                    VersionAnterior = currentVersionText,
                    VersionFinal = versionAnterior,
                    RutaOrigen = source.RutaOrigenActiva,
                    SinCambios = false,
                    ScriptsAplicados = applied
                };
            }
            finally
            {
                await ReleaseLockAsync(cn, lockHandle, token);
            }
        }, "No se pudieron aplicar las actualizaciones pendientes.", ct);

    private UpdateSource ResolveSource(ActualizacionesSettingsDto settings, bool validateAvailability, bool forceLocal = false)
    {
        var rutaLocal = RutaLocalUpdates;
        var rutaRed = NormalizePath(settings.RutaRed);
        var rutaRedDisponible = !string.IsNullOrWhiteSpace(rutaRed) && Directory.Exists(rutaRed);

        if (!Directory.Exists(rutaLocal))
            Directory.CreateDirectory(rutaLocal);

        if (!forceLocal && rutaRedDisponible)
        {
            return new UpdateSource
            {
                RutaLocal = rutaLocal,
                RutaRed = rutaRed,
                RutaRedDisponible = true,
                UsaRutaRed = true,
                RutaOrigenActiva = rutaRed
            };
        }

        if (validateAvailability && !forceLocal && !string.IsNullOrWhiteSpace(rutaRed) && !rutaRedDisponible)
            throw new InvalidOperationException("La ruta de red configurada para actualizaciones no está disponible.");

        return new UpdateSource
        {
            RutaLocal = rutaLocal,
            RutaRed = rutaRed,
            RutaRedDisponible = rutaRedDisponible,
            UsaRutaRed = false,
            RutaOrigenActiva = rutaLocal
        };
    }

    private static IReadOnlyList<ActualizacionScriptDto> GetScripts(UpdateSource source, bool requireAvailability)
    {
        var basePath = source.RutaOrigenActiva;

        if (requireAvailability && !Directory.Exists(basePath))
            throw new InvalidOperationException($"No existe la carpeta de actualizaciones: {basePath}");

        if (!Directory.Exists(basePath))
            return [];

        var files = Directory
            .GetFiles(basePath, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(static path => TryParseScript(path, out var script) ? script : null)
            .Where(static script => script is not null)
            .Select(static script => script!)
            .OrderBy(x => x.FechaVersion)
            .ThenBy(x => x.Correlativo)
            .ThenBy(x => x.Archivo, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicates = files
            .GroupBy(x => x.VersionKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Hay más de un script para la misma versión: {string.Join(", ", duplicates)}.");

        return files;
    }

    private static ActualizacionScriptDto ParseScript(string path)
    {
        var fileName = Path.GetFileName(path);
        var match = Regex.Match(fileName, @"^(?<date>\d{4}-\d{2}-\d{2})-(?<seq>\d{3})__(?<desc>.+)\.sql$", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException($"El archivo {fileName} no cumple el formato AAAA-MM-DD-NNN__descripcion.sql.");

        var versionDate = DateTime.ParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sequence = int.Parse(match.Groups["seq"].Value, CultureInfo.InvariantCulture);
        var description = match.Groups["desc"].Value.Replace('_', ' ').Trim();

        return new ActualizacionScriptDto
        {
            VersionKey = $"{match.Groups["date"].Value}-{match.Groups["seq"].Value}",
            FechaVersion = versionDate,
            Correlativo = sequence,
            Archivo = fileName,
            Descripcion = description,
            RutaCompleta = path
        };
    }

    private static bool TryParseScript(string path, out ActualizacionScriptDto? script)
    {
        var fileName = Path.GetFileName(path);
        var match = Regex.Match(fileName, @"^(?<date>\d{4}-\d{2}-\d{2})-(?<seq>\d{3})__(?<desc>.+)\.sql$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            script = null;
            return false;
        }

        script = ParseScript(path);
        return true;
    }

    private static IEnumerable<string> SplitSqlBatches(string sql)
    {
        return Regex.Split(sql, @"^\s*GO\s*($|\-\-.*$)", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where((_, index) => index % 2 == 0)
            .Select(static part => part.Trim())
            .Where(static part => !string.IsNullOrWhiteSpace(part));
    }

    private static ScriptVersion? ParseVersionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var versionMatch = Regex.Match(trimmed, @"^(?<date>\d{4}-\d{2}-\d{2})-(?<seq>\d{3})$", RegexOptions.IgnoreCase);
        if (versionMatch.Success)
        {
            return new ScriptVersion(
                DateTime.ParseExact(versionMatch.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                int.Parse(versionMatch.Groups["seq"].Value, CultureInfo.InvariantCulture),
                trimmed);
        }

        if (DateTime.TryParseExact(trimmed, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return new ScriptVersion(exact.Date, 999, trimmed);

        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
            return new ScriptVersion(isoDate.Date, 999, trimmed);

        if (DateTime.TryParse(trimmed, CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out var parsed))
            return new ScriptVersion(parsed.Date, 999, trimmed);

        return null;
    }

    private static string NormalizeVersionText(ScriptVersion? version)
        => version?.RawValue ?? "Sin versión aplicada";

    private static int CompareScriptToVersion(ActualizacionScriptDto script, ScriptVersion current)
    {
        var byDate = DateTime.Compare(script.FechaVersion.Date, current.Date.Date);
        if (byDate != 0)
            return byDate;

        return script.Correlativo.CompareTo(current.Sequence);
    }

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();

    private static string NormalizeActor(string? value, string fallback, int maxLength)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return source.Length <= maxLength ? source : source[..maxLength];
    }

    private async Task<string> AcquireLockAsync(SqlConnection cn, CancellationToken ct)
    {
        var activeSession = sessionService.GetActiveSession();
        var resource = $"{LockResourcePrefix}:{activeSession?.Servidor}:{activeSession?.BaseDatos}".ToUpperInvariant();

        const string sql = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 10000;
            SELECT @result;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Resource", resource);
        var result = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        if (result < 0)
            throw new InvalidOperationException("Otra instancia ya está aplicando actualizaciones sobre esta base. Intentá nuevamente en unos segundos.");

        return resource;
    }

    private static async Task ReleaseLockAsync(SqlConnection cn, string resource, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resource))
            return;

        const string sql = """
            EXEC sp_releaseapplock
                @Resource = @Resource,
                @LockOwner = 'Session';
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Resource", resource);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> ResolveDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux')
            ORDER BY name
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static async Task EnsureHistoryTableAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.AUX_ACTUALIZACION_HIST', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AUX_ACTUALIZACION_HIST
                (
                    IdActualizacionHist int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    FechaHora datetime NOT NULL CONSTRAINT DF_AUX_ACTUALIZACION_HIST_FechaHora DEFAULT (GETDATE()),
                    VersionAnterior nvarchar(20) NOT NULL,
                    VersionNueva nvarchar(20) NOT NULL,
                    ScriptArchivo nvarchar(260) NOT NULL,
                    RutaOrigen nvarchar(260) NOT NULL,
                    Resultado nvarchar(20) NOT NULL,
                    Observacion nvarchar(500) NOT NULL,
                    Usuario nvarchar(50) NOT NULL,
                    Pc nvarchar(100) NOT NULL,
                    ErrorDetalle nvarchar(max) NULL
                );

                CREATE INDEX IX_AUX_ACTUALIZACION_HIST_FechaHora
                    ON dbo.AUX_ACTUALIZACION_HIST (FechaHora DESC);
            END
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> GetConfigValueAsync(SqlConnection cn, string detailColumn, string key, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", key.ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        var valor = GetString(rd, 0);
        var aux = GetString(rd, 1);
        return ResolveStoredValue(valor, aux);
    }

    private static async Task UpsertConfigValueAsync(SqlConnection cn, string detailColumn, string key, string value, CancellationToken ct)
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

    private static async Task InsertHistoryAsync(
        SqlConnection cn,
        ActualizacionScriptDto script,
        string versionAnterior,
        string versionNueva,
        string rutaOrigen,
        string resultado,
        string observacion,
        string usuario,
        string pc,
        string? errorDetalle,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.AUX_ACTUALIZACION_HIST
            (
                VersionAnterior,
                VersionNueva,
                ScriptArchivo,
                RutaOrigen,
                Resultado,
                Observacion,
                Usuario,
                Pc,
                ErrorDetalle
            )
            VALUES
            (
                @VersionAnterior,
                @VersionNueva,
                @ScriptArchivo,
                @RutaOrigen,
                @Resultado,
                @Observacion,
                @Usuario,
                @Pc,
                @ErrorDetalle
            )
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@VersionAnterior", versionAnterior);
        cmd.Parameters.AddWithValue("@VersionNueva", versionNueva);
        cmd.Parameters.AddWithValue("@ScriptArchivo", script.Archivo);
        cmd.Parameters.AddWithValue("@RutaOrigen", rutaOrigen);
        cmd.Parameters.AddWithValue("@Resultado", resultado);
        cmd.Parameters.AddWithValue("@Observacion", observacion);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        cmd.Parameters.AddWithValue("@Pc", pc);
        cmd.Parameters.AddWithValue("@ErrorDetalle", DbNullable(errorDetalle));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<ActualizacionHistorialDto>> GetHistoryAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (100)
                IdActualizacionHist,
                FechaHora,
                ISNULL(VersionAnterior, ''),
                ISNULL(VersionNueva, ''),
                ISNULL(ScriptArchivo, ''),
                ISNULL(RutaOrigen, ''),
                ISNULL(Resultado, ''),
                ISNULL(Observacion, ''),
                ISNULL(Usuario, ''),
                ISNULL(Pc, ''),
                ISNULL(ErrorDetalle, '')
            FROM dbo.AUX_ACTUALIZACION_HIST
            ORDER BY FechaHora DESC, IdActualizacionHist DESC
            """;

        var items = new List<ActualizacionHistorialDto>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new ActualizacionHistorialDto
            {
                IdActualizacionHist = GetInt(rd, 0),
                FechaHora = rd.GetDateTime(1),
                VersionAnterior = GetString(rd, 2),
                VersionNueva = GetString(rd, 3),
                ScriptArchivo = GetString(rd, 4),
                RutaOrigen = GetString(rd, 5),
                Resultado = GetString(rd, 6),
                Observacion = GetString(rd, 7),
                Usuario = GetString(rd, 8),
                Pc = GetString(rd, 9),
                ErrorDetalle = GetString(rd, 10)
            });
        }

        return items;
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length <= 150)
            return (normalized, string.Empty);

        return (string.Empty, normalized);
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index));

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException("La base activa no tiene completo el esquema esperado para el módulo de actualizaciones.", incidentId, ex);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private sealed class UpdateSource
    {
        public string RutaLocal { get; init; } = string.Empty;
        public string RutaRed { get; init; } = string.Empty;
        public string RutaOrigenActiva { get; init; } = string.Empty;
        public bool RutaRedDisponible { get; init; }
        public bool UsaRutaRed { get; init; }
    }

    private readonly record struct ScriptVersion(DateTime Date, int Sequence, string RawValue);
}
