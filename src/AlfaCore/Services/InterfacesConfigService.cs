using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Net;

namespace AlfaCore.Services;

public sealed class InterfacesConfigService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IInterfacesConfigService
{
    private const string ConfigGroup = "INTERFACES";
    private const string DefaultDestinoTipo = "FTP";
    private const string DefaultDestinoNombre = "Recepción principal";
    private const string DefaultRutaBase = "/";
    private const string DefaultFtpHost = "alfanet.ddns.net";
    private const int DefaultFtpPuerto = 21;
    private const string DefaultFtpUsuario = "ftpalfa";
    private const string DefaultFtpClave = "24681012";
    private const string DefaultIaFolder = @"C:\dev\IA_ProcesarDocumentos";
    private const string DefaultIaScriptName = "lector_facturas_to_json_v5.py";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<InterfacesUploadSettingsDto> GetUploadSettingsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "GetUploadSettings", async token =>
        {
            var activeSession = sessionService.GetActiveSession();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);

            var sql = $"""
                SELECT
                    UPPER(LTRIM(RTRIM(CLAVE))),
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN
                (
                    'INTERFACESRECEPCIONTIPO',
                    'INTERFACESRECEPCIONNOMBRE',
                    'INTERFACESRECEPCIONRUTA',
                    'INTERFACESFTPHOST',
                    'INTERFACESFTPPUERTO',
                    'INTERFACESFTPUSUARIO',
                    'INTERFACESFTPCLAVE',
                    'INTERFACESFTPPASIVO',
                    'INTERFACESESTADOINICIAL',
                    'INTERFACESTAMANOMAXIMOMB',
                    'INTERFACESEXTENSIONESPERMITIDAS'
                )
                """;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var key = GetString(rd, 0);
                var value = ResolveStoredValue(GetString(rd, 1), GetString(rd, 2));
                values[key] = value;
            }

            var estadoInicial = ReadValue(values, "INTERFACESESTADOINICIAL", "A_PROCESAR");
            var tamanoMb = 25;
            if (int.TryParse(ReadValue(values, "INTERFACESTAMANOMAXIMOMB", "25"), out var parsedMb) && parsedMb > 0)
                tamanoMb = parsedMb;

            var extensiones = ParseExtensions(ReadValue(values, "INTERFACESEXTENSIONESPERMITIDAS", ".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.csv,.txt"));
            var ftpPuerto = DefaultFtpPuerto;
            if (int.TryParse(ReadValue(values, "INTERFACESFTPPUERTO", DefaultFtpPuerto.ToString()), out var parsedPuerto) && parsedPuerto > 0)
                ftpPuerto = parsedPuerto;

            var ftpPasivo = !string.Equals(ReadValue(values, "INTERFACESFTPPASIVO", "SI"), "NO", StringComparison.OrdinalIgnoreCase);

            return new InterfacesUploadSettingsDto
            {
                DestinoTipo = ReadValue(values, "INTERFACESRECEPCIONTIPO", DefaultDestinoTipo).Trim().ToUpperInvariant(),
                DestinoNombre = ReadValue(values, "INTERFACESRECEPCIONNOMBRE", DefaultDestinoNombre).Trim(),
                RutaBase = ResolveRutaBase(ReadValue(values, "INTERFACESRECEPCIONRUTA", string.Empty), activeSession, ReadValue(values, "INTERFACESRECEPCIONTIPO", DefaultDestinoTipo)),
                FtpHost = ReadValue(values, "INTERFACESFTPHOST", DefaultFtpHost).Trim(),
                FtpPuerto = ftpPuerto,
                FtpUsuario = ReadValue(values, "INTERFACESFTPUSUARIO", DefaultFtpUsuario).Trim(),
                FtpClave = ReadValue(values, "INTERFACESFTPCLAVE", DefaultFtpClave).Trim(),
                FtpModoPasivo = ftpPasivo,
                EstadoInicialCodigo = estadoInicial.Trim(),
                TamanoMaximoMb = tamanoMb,
                ExtensionesPermitidas = extensiones
            };
        }, "No se pudo cargar la configuración de Interfaces.", ct);

    public async Task SaveUploadSettingsAsync(InterfacesUploadSettingsDto settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await ExecuteLoggedAsync("Interfaces", "SaveUploadSettings", async token =>
        {
            var normalized = Normalize(settings, sessionService.GetActiveSession());

            await EnsureDestinationExistsAsync(normalized, token);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildItems(normalized))
            {
                var stored = SplitStoredValue(item.Value);
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

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Key.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Key);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Interfaces",
                "SaveUploadSettings",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de recepción documental actualizada.",
                new
                {
                    normalized.DestinoTipo,
                    normalized.DestinoNombre,
                    normalized.RutaBase,
                    normalized.FtpHost,
                    normalized.FtpPuerto,
                    normalized.FtpUsuario,
                    normalized.FtpModoPasivo,
                    normalized.EstadoInicialCodigo,
                    normalized.TamanoMaximoMb
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de Interfaces.", ct);
    }

    public Task<InterfacesCompraIaSettingsDto> GetCompraIaSettingsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "GetCompraIaSettings", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);

            var sql = $"""
                SELECT
                    UPPER(LTRIM(RTRIM(CLAVE))),
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN
                (
                    'INTERFACESIACOMPRASHABILITADO',
                    'INTERFACESIACOMPRASPYTHONEXE',
                    'INTERFACESIACOMPRASSCRIPTPATH',
                    'INTERFACESIACOMPRASWORKDIR',
                    'INTERFACESIACOMPRASWORKERHABILITADO',
                    'INTERFACESIACOMPRASWORKERMAXPARALELO',
                    'INTERFACESIACOMPRASWORKERINTERVALOSEGUNDOS'
                )
                """;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var key = GetString(rd, 0);
                var value = ResolveStoredValue(GetString(rd, 1), GetString(rd, 2));
                values[key] = value;
            }

            var defaultScriptPath = Path.Combine(DefaultIaFolder, DefaultIaScriptName);
            var defaultWorkDir = Directory.Exists(DefaultIaFolder) ? DefaultIaFolder : string.Empty;

            return new InterfacesCompraIaSettingsDto
            {
                Habilitado = string.Equals(ReadValue(values, "INTERFACESIACOMPRASHABILITADO", "NO"), "SI", StringComparison.OrdinalIgnoreCase),
                PythonExe = ReadValue(values, "INTERFACESIACOMPRASPYTHONEXE", string.Empty).Trim(),
                ScriptPath = ReadValue(values, "INTERFACESIACOMPRASSCRIPTPATH", File.Exists(defaultScriptPath) ? defaultScriptPath : string.Empty).Trim(),
                WorkDir = ReadValue(values, "INTERFACESIACOMPRASWORKDIR", defaultWorkDir).Trim(),
                WorkerHabilitado = !string.Equals(ReadValue(values, "INTERFACESIACOMPRASWORKERHABILITADO", "SI"), "NO", StringComparison.OrdinalIgnoreCase),
                WorkerMaxParalelo = ParsePositiveInt(ReadValue(values, "INTERFACESIACOMPRASWORKERMAXPARALELO", "1"), 1),
                WorkerIntervaloSegundos = ParsePositiveInt(ReadValue(values, "INTERFACESIACOMPRASWORKERINTERVALOSEGUNDOS", "10"), 10)
            };
        }, "No se pudo cargar la configuración de lectura automática de compras.", ct);

    public async Task SaveCompraIaSettingsAsync(InterfacesCompraIaSettingsDto settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await ExecuteLoggedAsync("Interfaces", "SaveCompraIaSettings", async token =>
        {
            var normalized = Normalize(settings);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildItems(normalized))
            {
                var stored = SplitStoredValue(item.Value);
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

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Key.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Key);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Interfaces",
                "SaveCompraIaSettings",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de lectura automática de compras actualizada.",
                new
                {
                    normalized.Habilitado,
                    normalized.PythonExe,
                    normalized.ScriptPath,
                    normalized.WorkDir
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de lectura automática de compras.", ct);
    }

    public Task<InterfacesCompraIaProbeResultDto> ProbeCompraIaSettingsAsync(InterfacesCompraIaSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "ProbeCompraIaSettings", async token =>
        {
            var normalized = Normalize(settings);

            if (!normalized.Habilitado)
                throw new InvalidOperationException("La detección automática de compras está deshabilitada. Activala antes de probar la configuración.");
            if (string.IsNullOrWhiteSpace(normalized.PythonExe) || !File.Exists(normalized.PythonExe))
                throw new InvalidOperationException("No se encontró el ejecutable Python configurado.");
            if (string.IsNullOrWhiteSpace(normalized.ScriptPath) || !File.Exists(normalized.ScriptPath))
                throw new InvalidOperationException("No se encontró el script configurado para la lectura automática.");
            if (string.IsNullOrWhiteSpace(normalized.WorkDir) || !Directory.Exists(normalized.WorkDir))
                throw new InvalidOperationException("No existe la carpeta de trabajo configurada para la lectura automática.");

            var version = await GetPythonVersionAsync(normalized.PythonExe, normalized.WorkDir, token);

            return new InterfacesCompraIaProbeResultDto
            {
                PythonVersion = version,
                ScriptPath = normalized.ScriptPath,
                WorkDir = normalized.WorkDir
            };
        }, "No se pudo probar la configuración de lectura automática de compras.", ct);

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

    private static IReadOnlyList<string> ParseExtensions(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item => item.StartsWith('.') ? item.Trim().ToLowerInvariant() : "." + item.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Key, string Value)> BuildItems(InterfacesUploadSettingsDto settings)
    {
        yield return ("InterfacesRecepcionTipo", settings.DestinoTipo);
        yield return ("InterfacesRecepcionNombre", settings.DestinoNombre);
        yield return ("InterfacesRecepcionRuta", settings.RutaBase);
        yield return ("InterfacesFtpHost", settings.FtpHost);
        yield return ("InterfacesFtpPuerto", settings.FtpPuerto.ToString());
        yield return ("InterfacesFtpUsuario", settings.FtpUsuario);
        yield return ("InterfacesFtpClave", settings.FtpClave);
        yield return ("InterfacesFtpPasivo", settings.FtpModoPasivo ? "SI" : "NO");
        yield return ("InterfacesEstadoInicial", settings.EstadoInicialCodigo);
        yield return ("InterfacesTamanoMaximoMb", settings.TamanoMaximoMb.ToString());
        yield return ("InterfacesExtensionesPermitidas", string.Join(",", settings.ExtensionesPermitidas));
    }

    private static IEnumerable<(string Key, string Value)> BuildItems(InterfacesCompraIaSettingsDto settings)
    {
        yield return ("InterfacesIaComprasHabilitado", settings.Habilitado ? "SI" : "NO");
        yield return ("InterfacesIaComprasPythonExe", settings.PythonExe);
        yield return ("InterfacesIaComprasScriptPath", settings.ScriptPath);
        yield return ("InterfacesIaComprasWorkDir", settings.WorkDir);
        yield return ("InterfacesIaComprasWorkerHabilitado", settings.WorkerHabilitado ? "SI" : "NO");
        yield return ("InterfacesIaComprasWorkerMaxParalelo", settings.WorkerMaxParalelo.ToString());
        yield return ("InterfacesIaComprasWorkerIntervaloSegundos", settings.WorkerIntervaloSegundos.ToString());
    }

    private static InterfacesUploadSettingsDto Normalize(InterfacesUploadSettingsDto settings, SessionDto? activeSession)
    {
        var tipo = string.Equals(settings.DestinoTipo, "CARPETA", StringComparison.OrdinalIgnoreCase) ? "CARPETA" : "FTP";
        var ruta = ResolveRutaBase(settings.RutaBase, activeSession, tipo);
        if (tipo == "FTP")
            ruta = InterfacesUploadSettingsDto.NormalizeFtpPath(ruta);

        var extensiones = ParseExtensions(string.Join(",", settings.ExtensionesPermitidas));
        if (extensiones.Count == 0)
            extensiones = ParseExtensions(".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.csv,.txt");

        return new InterfacesUploadSettingsDto
        {
            DestinoTipo = tipo,
            DestinoNombre = string.IsNullOrWhiteSpace(settings.DestinoNombre) ? DefaultDestinoNombre : settings.DestinoNombre.Trim(),
            RutaBase = ruta,
            FtpHost = string.IsNullOrWhiteSpace(settings.FtpHost) ? DefaultFtpHost : settings.FtpHost.Trim(),
            FtpPuerto = settings.FtpPuerto <= 0 ? DefaultFtpPuerto : settings.FtpPuerto,
            FtpUsuario = string.IsNullOrWhiteSpace(settings.FtpUsuario) ? DefaultFtpUsuario : settings.FtpUsuario.Trim(),
            FtpClave = string.IsNullOrWhiteSpace(settings.FtpClave) ? DefaultFtpClave : settings.FtpClave.Trim(),
            FtpModoPasivo = settings.FtpModoPasivo,
            EstadoInicialCodigo = string.IsNullOrWhiteSpace(settings.EstadoInicialCodigo) ? "A_PROCESAR" : settings.EstadoInicialCodigo.Trim(),
            TamanoMaximoMb = settings.TamanoMaximoMb <= 0 ? 25 : settings.TamanoMaximoMb,
            ExtensionesPermitidas = extensiones
        };
    }

    private static InterfacesCompraIaSettingsDto Normalize(InterfacesCompraIaSettingsDto settings)
    {
        var scriptPath = settings.ScriptPath?.Trim() ?? string.Empty;
        var workDir = settings.WorkDir?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            var defaultScriptPath = Path.Combine(DefaultIaFolder, DefaultIaScriptName);
            if (File.Exists(defaultScriptPath))
                scriptPath = defaultScriptPath;
        }

        if (string.IsNullOrWhiteSpace(workDir) && !string.IsNullOrWhiteSpace(scriptPath))
            workDir = Path.GetDirectoryName(scriptPath) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workDir) && Directory.Exists(DefaultIaFolder))
            workDir = DefaultIaFolder;

        return new InterfacesCompraIaSettingsDto
        {
            Habilitado = settings.Habilitado,
            PythonExe = settings.PythonExe?.Trim() ?? string.Empty,
            ScriptPath = scriptPath,
            WorkDir = workDir,
            WorkerHabilitado = settings.WorkerHabilitado,
            WorkerMaxParalelo = Math.Max(1, settings.WorkerMaxParalelo),
            WorkerIntervaloSegundos = Math.Max(3, settings.WorkerIntervaloSegundos)
        };
    }

    private static string ResolveRutaBase(string? rutaBase, SessionDto? activeSession, string? destinoTipo)
    {
        if (!string.IsNullOrWhiteSpace(rutaBase))
            return rutaBase.Trim();

        var baseName = BuildBaseFolderName(activeSession);
        var ftpDefault = $"/Z_CLIENTES/{baseName}/";
        var carpetaDefault = $@"\Z_CLIENTES\{baseName}";

        return string.Equals(destinoTipo, "CARPETA", StringComparison.OrdinalIgnoreCase)
            ? carpetaDefault
            : ftpDefault;
    }

    private static string BuildBaseFolderName(SessionDto? activeSession)
    {
        var raw = !string.IsNullOrWhiteSpace(activeSession?.BaseDatos)
            ? activeSession!.BaseDatos
            : !string.IsNullOrWhiteSpace(activeSession?.Nombre)
                ? activeSession!.Nombre
                : "BASE";

        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var clean = new string(raw.Trim().Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "BASE" : clean;
    }

    private static async Task EnsureDestinationExistsAsync(InterfacesUploadSettingsDto settings, CancellationToken ct)
    {
        if (settings.UsaFtp)
        {
            await EnsureFtpDirectoryExistsAsync(settings, ct);
            return;
        }

        var targetPath = settings.RutaBase.Trim();
        if (!string.IsNullOrWhiteSpace(targetPath))
            Directory.CreateDirectory(targetPath);
    }

    private static async Task EnsureFtpDirectoryExistsAsync(InterfacesUploadSettingsDto settings, CancellationToken ct)
    {
        var host = settings.FtpHost.Trim();
        if (string.IsNullOrWhiteSpace(host))
            return;

        var path = InterfacesUploadSettingsDto.NormalizeFtpPath(settings.RutaBase);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return;

        var current = string.Empty;
        foreach (var segment in segments)
        {
            current += "/" + segment;
#pragma warning disable SYSLIB0014
            var request = (FtpWebRequest)WebRequest.Create($"ftp://{host}:{Math.Max(1, settings.FtpPuerto)}{current}");
#pragma warning restore SYSLIB0014
            request.Method = WebRequestMethods.Ftp.MakeDirectory;
            request.Credentials = new NetworkCredential(settings.FtpUsuario, settings.FtpClave);
            request.UsePassive = settings.FtpModoPasivo;
            request.UseBinary = true;
            request.KeepAlive = false;

            try
            {
                using var response = (FtpWebResponse)await request.GetResponseAsync();
            }
            catch (WebException ex) when (ex.Response is FtpWebResponse ftpResponse &&
                                           (ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable ||
                                            ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFilenameNotAllowed))
            {
                ftpResponse.Dispose();
            }
        }
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static async Task<string> GetPythonVersionAsync(string pythonExe, string workDir, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "--version",
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("No se pudo iniciar el ejecutable Python configurado.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdOut = (await stdOutTask).Trim();
        var stdErr = (await stdErrTask).Trim();

        if (process.ExitCode != 0)
        {
            var detail = !string.IsNullOrWhiteSpace(stdErr) ? stdErr : stdOut;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? "El ejecutable Python configurado terminó con error."
                : $"El ejecutable Python configurado respondió con error: {detail}");
        }

        var version = !string.IsNullOrWhiteSpace(stdOut) ? stdOut : stdErr;
        return string.IsNullOrWhiteSpace(version) ? "Python verificado" : version;
    }

    private static string ReadValue(Dictionary<string, string> values, string key, string fallback = "")
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int ParsePositiveInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length > 150)
            return (string.Empty, normalized);

        return (normalized, string.Empty);
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

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
}
