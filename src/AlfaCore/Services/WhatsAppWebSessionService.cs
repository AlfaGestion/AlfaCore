using AlfaCore.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class WhatsAppWebSessionService(
    IWebHostEnvironment environment,
    ISessionService sessionService,
    IConversacionesConfigService configService,
    IAppEventService appEvents) : IWhatsAppWebSessionService
{
    private const string WorkerRelativeDir = "Node\\WhatsAppWebWorker";
    private const string WorkerScriptName = "worker.mjs";
    private const string StatusFileName = "status.json";
    private const string SessionsRootDir = "App_Data\\whatsapp-web";
    private const string AuthDirName = "auth";
    private const string OutboxDirName = "outbox";
    private const string ResultsDirName = "results";
    private const string InboxDirName = "inbox";

    /// <summary>
    /// Evita que dos clicks en "Conectar WhatsApp" para el mismo número (doble click, F5 durante la
    /// espera de 20s, o dos pestañas del navegador) disparen dos procesos Node concurrentes -- cada
    /// intento que no llegaba a persistirse a tiempo generaba un WebInstanceName nuevo y dejaba el
    /// proceso Node anterior corriendo de fondo sin que nada lo mate (huérfano). Clave compuesta por
    /// BaseId porque IdNumero no es único entre bases distintas (una sola instancia de AlfaCore
    /// atiende varias bases).
    /// </summary>
    private static readonly ConcurrentDictionary<(int BaseId, int IdNumero), byte> StartInProgress = new();

    public async Task<ConversacionWhatsAppNumeroDto> StartSessionAsync(int idNumero, bool includeTextCode, CancellationToken ct = default)
    {
        var baseId = sessionService.GetActiveSession()?.BaseId ?? 0;
        var guardKey = (baseId, idNumero);
        if (!StartInProgress.TryAdd(guardKey, 0))
        {
            throw new InvalidOperationException(
                "Ya hay una conexión de WhatsApp Web en curso para este número. Esperá a que termine antes de volver a intentarlo.");
        }

        try
        {
            var numero = await GetNumeroAsync(idNumero, ct);
            EnsureWorkerFilesExist();

            if (string.Equals(numero.WebSessionMode, ConversacionWhatsAppWebSessionModes.PhoneNumber, StringComparison.OrdinalIgnoreCase)
                && includeTextCode
                && string.IsNullOrWhiteSpace(numero.WebPhoneNumber))
            {
                throw new InvalidOperationException("Para iniciar por número tenés que cargar primero el teléfono de la sesión Web.");
            }

            if (string.IsNullOrWhiteSpace(numero.WebInstanceName))
                numero.WebInstanceName = $"waweb-{Guid.NewGuid():N}"[..14];

            var sessionDir = EnsureSessionDirectory(numero.WebInstanceName);
            var statusFile = Path.Combine(sessionDir, StatusFileName);
            StopProcessIfRunning(statusFile);
            TryDeleteFile(statusFile);
            PrepareSessionDirectory(sessionDir, clearAuth: true);

            // Persistimos el WebInstanceName ANTES de arrancar el proceso: si este intento se cae por
            // timeout (ver WaitForStatusAndPersistAsync) sin haber escrito status.json, el próximo
            // intento reencuentra este mismo WebInstanceName en vez de generar uno nuevo -- así
            // StopProcessIfRunning() de la próxima corrida puede matar el proceso viejo en vez de
            // dejarlo huérfano corriendo indefinidamente.
            await configService.SaveWhatsAppNumeroWebSessionAsync(numero, ct);

            var args = BuildStartArguments(sessionDir, numero.WebSessionMode, numero.WebPhoneNumber, includeTextCode, numero.WebInstanceName);
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = args,
                WorkingDirectory = GetWorkerDirectory(),
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "No se pudo iniciar el proceso de WhatsApp Web: no se encontró \"node\" en este servidor. " +
                    "Instalá Node.js y ejecutá \"npm install\" en Node/WhatsAppWebWorker antes de conectar un número. " +
                    $"Detalle técnico: {ex.Message}");
            }

            var updated = await WaitForStatusAndPersistAsync(numero, statusFile, requireInteractiveArtifacts: true, ct);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "StartWhatsAppWebSession",
                "CONV_WHATSAPP_NUMEROS",
                idNumero.ToString(CultureInfo.InvariantCulture),
                "Sesión de WhatsApp Web iniciada.",
                new { idNumero, updated.WebInstanceName, updated.WebSessionMode, includeTextCode, updated.WebRuntimeState },
                ct);

            return updated;
        }
        finally
        {
            StartInProgress.TryRemove(guardKey, out _);
        }
    }

    public async Task<ConversacionWhatsAppNumeroDto> RefreshSessionAsync(int idNumero, CancellationToken ct = default)
    {
        var numero = await GetNumeroAsync(idNumero, ct);
        if (string.IsNullOrWhiteSpace(numero.WebInstanceName))
            return numero;

        var statusFile = Path.Combine(EnsureSessionDirectory(numero.WebInstanceName), StatusFileName);
        return await LoadStatusAndPersistAsync(numero, statusFile, ct);
    }

    public async Task<ConversacionWhatsAppNumeroDto> StopSessionAsync(int idNumero, CancellationToken ct = default)
    {
        var numero = await GetNumeroAsync(idNumero, ct);
        if (string.IsNullOrWhiteSpace(numero.WebInstanceName))
            return numero;

        var previousInstanceName = numero.WebInstanceName;
        var sessionDir = EnsureSessionDirectory(numero.WebInstanceName);
        var statusFile = Path.Combine(sessionDir, StatusFileName);
        StopProcessIfRunning(statusFile);

        numero.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.Disconnected;
        numero.WebRuntimeState = "STOPPED";
        numero.WebLastError = string.Empty;
        numero.WebWorkerProcessId = null;
        numero.WebRuntimeUpdatedAtUtc = DateTime.UtcNow;
        numero.WebPairingCode = string.Empty;
        numero.WebPairingQrPayload = string.Empty;
        numero.WebPairingQrDataUrl = string.Empty;
        numero.WebPairingGeneratedAtUtc = null;
        numero.WebPairingExpiresAtUtc = null;
        numero.WebPairingToken = string.Empty;

        // ResolveWhatsAppDeliveryProviderForNumero() en ConversacionesService rutea por WhatsApp Web
        // apenas WebInstanceName no está vacío, sin mirar WebSessionStatus -- si no lo limpiamos acá,
        // "Desvincular" deja el número visualmente Disconnected pero cada envío sigue intentando (y
        // fallando) contra un worker Web que ya no existe, en vez de volver a Meta Cloud API.
        numero.WebInstanceName = string.Empty;

        await configService.SaveWhatsAppNumeroWebSessionAsync(numero, ct);

        await appEvents.LogAuditAsync(
            "Conversaciones",
            "StopWhatsAppWebSession",
            "CONV_WHATSAPP_NUMEROS",
            idNumero.ToString(CultureInfo.InvariantCulture),
            "Sesión de WhatsApp Web detenida.",
            new { idNumero, previousInstanceName },
            ct);

        return numero;
    }

    public async Task<ConversacionWhatsAppWebSendResultDto> SendTextAsync(int? idNumeroWhatsApp, string phone, string text, string? replyToMessageId, CancellationToken ct = default)
    {
        if (!idNumeroWhatsApp.HasValue || idNumeroWhatsApp.Value <= 0)
            throw new InvalidOperationException("La conversación no tiene un número de WhatsApp Web asociado.");

        var numero = await GetNumeroAsync(idNumeroWhatsApp.Value, ct);
        if (string.IsNullOrWhiteSpace(numero.WebInstanceName))
            throw new InvalidOperationException("No hay una instancia de WhatsApp Web configurada para este número.");

        var sessionDir = EnsureSessionDirectory(numero.WebInstanceName);
        var outboxDir = Path.Combine(sessionDir, OutboxDirName);
        var resultsDir = Path.Combine(sessionDir, ResultsDirName);
        Directory.CreateDirectory(outboxDir);
        Directory.CreateDirectory(resultsDir);

        var commandId = Guid.NewGuid().ToString("N");
        var commandPath = Path.Combine(outboxDir, $"{commandId}.json");
        var resultPath = Path.Combine(resultsDir, $"{commandId}.json");
        var payload = new
        {
            id = commandId,
            type = "send_text",
            phone = phone.Trim(),
            text = text,
            replyToMessageId = replyToMessageId ?? string.Empty,
            createdAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await File.WriteAllTextAsync(commandPath, JsonSerializer.Serialize(payload, JsonOptions), ct);

        var deadline = DateTime.UtcNow.AddSeconds(35);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(resultPath))
            {
                var json = await File.ReadAllTextAsync(resultPath, ct);
                TryDeleteFile(resultPath);
                var result = JsonSerializer.Deserialize<WhatsAppWebCommandResult>(json, JsonOptions) ?? new WhatsAppWebCommandResult();
                if (!string.IsNullOrWhiteSpace(result.Error))
                    throw new InvalidOperationException(result.Error);

                return new ConversacionWhatsAppWebSendResultDto
                {
                    ExternalMessageId = result.ExternalMessageId ?? string.Empty,
                    EstadoEnvio = string.IsNullOrWhiteSpace(result.State) ? "ENVIADO" : result.State,
                    PayloadJson = json
                };
            }

            await Task.Delay(500, ct);
        }

        throw new InvalidOperationException("WhatsApp Web no respondió a tiempo al comando de envío.");
    }

    private async Task<ConversacionWhatsAppNumeroDto> WaitForStatusAndPersistAsync(
        ConversacionWhatsAppNumeroDto numero,
        string statusFile,
        bool requireInteractiveArtifacts,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(statusFile))
            {
                var updated = await LoadStatusAndPersistAsync(numero, statusFile, ct);
                var hasInteractiveArtifacts = updated.HasWebPairingQr || updated.HasWebPairingCode || updated.IsWebSessionReady;
                if (!requireInteractiveArtifacts || hasInteractiveArtifacts)
                    return updated;

                if (string.Equals(updated.WebRuntimeState, "DISCONNECTED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(updated.WebRuntimeState, "ERROR", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(updated.WebRuntimeState, "CLOSED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"WhatsApp Web no pudo iniciar la sesión del número '{updated.Nombre}'. Estado: {updated.WebRuntimeState}. " +
                        $"{(string.IsNullOrWhiteSpace(updated.WebLastError) ? string.Empty : $"Detalle: {updated.WebLastError}")}".Trim());
                }
            }

            await Task.Delay(500, ct);
        }

        throw new InvalidOperationException("No se recibió el estado inicial de WhatsApp Web a tiempo. Revisá que Node y las dependencias del worker estén instaladas correctamente.");
    }

    private async Task<ConversacionWhatsAppNumeroDto> LoadStatusAndPersistAsync(ConversacionWhatsAppNumeroDto numero, string statusFile, CancellationToken ct)
    {
        if (!File.Exists(statusFile))
            return numero;

        var json = await File.ReadAllTextAsync(statusFile, ct);
        var status = JsonSerializer.Deserialize<WhatsAppWebWorkerStatus>(json, JsonOptions) ?? new WhatsAppWebWorkerStatus();
        ApplyStatus(numero, status);
        await configService.SaveWhatsAppNumeroWebSessionAsync(numero, ct);
        return numero;
    }

    private async Task<ConversacionWhatsAppNumeroDto> GetNumeroAsync(int idNumero, CancellationToken ct)
    {
        var numero = await configService.GetWhatsAppNumeroAsync(idNumero, ct);
        if (numero is null)
            throw new InvalidOperationException("No se encontró el número de WhatsApp seleccionado.");

        return numero;
    }

    private static void ApplyStatus(ConversacionWhatsAppNumeroDto numero, WhatsAppWebWorkerStatus status)
    {
        numero.WebRuntimeState = status.State;
        numero.WebLastError = status.Error;
        numero.WebWorkerProcessId = status.ProcessId;
        numero.WebRuntimeUpdatedAtUtc = status.LastUpdatedAtUtc;

        if (string.Equals(status.State, "CONNECTED", StringComparison.OrdinalIgnoreCase))
            numero.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.Connected;
        else if (string.Equals(status.State, "QR_READY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.State, "PAIRING_CODE_READY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.State, "STARTING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.State, "RECONNECTING", StringComparison.OrdinalIgnoreCase))
            numero.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.PendingQr;
        else if (!string.IsNullOrWhiteSpace(status.State))
            numero.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.Disconnected;

        numero.WebPairingQrPayload = status.QrPayload ?? string.Empty;
        numero.WebPairingCode = status.PairingCode ?? string.Empty;
        numero.WebPairingGeneratedAtUtc = status.GeneratedAtUtc;
        numero.WebPairingExpiresAtUtc = status.ExpiresAtUtc;
        numero.WebPairingToken = status.SessionId ?? string.Empty;
        numero.WebPairingQrDataUrl = string.IsNullOrWhiteSpace(status.QrPayload)
            ? string.Empty
            : BuildQrCodeDataUrl(status.QrPayload);

        if (string.Equals(status.State, "CONNECTED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(status.PhoneNumber))
        {
            numero.WebPhoneNumber = status.PhoneNumber;
        }
    }

    /// <summary>
    /// Mismo motivo que GetWorkerDirectory(): AppContext.BaseDirectory es estable sin importar el cwd
    /// con el que se lanzó el proceso. Esta carpeta la calculan por separado StartSessionAsync (crea
    /// la sesión y arranca el worker con este path como argumento), y RefreshSessionAsync/
    /// SendTextAsync/StopSessionAsync (la recalculan en cada llamada) -- si el proceso que atiende un
    /// request usa un ContentRootPath distinto al que tenía el proceso que arrancó el worker (ej. la
    /// app se reinició con otro cwd mientras un worker seguía vivo de antes), cada lado termina
    /// mirando una carpeta física distinta: el comando de envío se escribe en una carpeta que el
    /// worker real nunca lee, y el timeout de 35s de SendTextAsync se dispara siempre. Usar
    /// AppContext.BaseDirectory en los dos lados elimina esa dependencia del cwd del lanzador.
    /// </summary>
    private string EnsureSessionDirectory(string instanceName)
    {
        var safeInstanceName = SanitizeInstanceName(instanceName);
        var baseId = sessionService.GetActiveSession()?.BaseId ?? 0;
        var path = Path.Combine(AppContext.BaseDirectory, SessionsRootDir, baseId.ToString(CultureInfo.InvariantCulture), safeInstanceName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// WebInstanceName puede venir de un campo editable ("Opciones avanzadas"). Se usa directo en un
    /// Path.Combine para la carpeta de sesión, así que no puede contener separadores de path ni "..":
    /// sólo se permiten letras, números, guion y guion bajo, truncado a un largo razonable.
    /// </summary>
    private static string SanitizeInstanceName(string instanceName)
    {
        var safe = new string((instanceName ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (safe.Length == 0)
            throw new InvalidOperationException("La instancia de WhatsApp Web no tiene un nombre válido. Volvé a generar la conexión.");

        return safe.Length > 40 ? safe[..40] : safe;
    }

    /// <summary>
    /// AppContext.BaseDirectory es la ubicación física real del ensamblado (exe/dll) -- estable sin
    /// importar desde qué carpeta se lanzó el proceso ni si corre como Windows Service. A diferencia
    /// de IWebHostEnvironment.ContentRootPath (por defecto el cwd del proceso al arrancar): con
    /// AlfaCore.exe lanzado desde su propia carpeta de salida (doble click, servicio, tarea
    /// programada) ContentRootPath coincide y esto no cambia nada; pero si se lanza con otro cwd
    /// (ej. un acceso directo o un `Start-Process -WorkingDirectory` distinto), ContentRootPath ya
    /// no apunta a la carpeta de salida real y "Node\WhatsAppWebWorker" se resuelve mal.
    /// El worker (worker.mjs + package.json/package-lock.json, ver AlfaCore.csproj) se copia a esa
    /// misma carpeta de salida en cada build/publish -- node_modules NO se copia ahí (no se
    /// commitea a git); hace falta `npm ci` una vez dentro de esa carpeta de salida, ver
    /// docs/ui/conversaciones-configuracion-redesign.md.
    /// En Development, si el build todavía no copió el worker a la salida (o se corre con
    /// `dotnet run`/F5 antes del primer build), se cae al árbol fuente del proyecto -- mismo
    /// patrón ya usado en Program.cs para resolver wwwroot/scopedcss en desarrollo.
    /// </summary>
    private string GetWorkerDirectory()
    {
        var outputWorkerDir = Path.Combine(AppContext.BaseDirectory, WorkerRelativeDir);
        if (File.Exists(Path.Combine(outputWorkerDir, WorkerScriptName)))
            return outputWorkerDir;

        if (environment.IsDevelopment())
        {
            var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var sourceWorkerDir = Path.Combine(projectDir, WorkerRelativeDir);
            if (File.Exists(Path.Combine(sourceWorkerDir, WorkerScriptName)))
                return sourceWorkerDir;
        }

        return outputWorkerDir;
    }

    private void EnsureWorkerFilesExist()
    {
        var workerDirectory = GetWorkerDirectory();
        var workerScript = Path.Combine(workerDirectory, WorkerScriptName);
        if (!File.Exists(workerScript))
            throw new InvalidOperationException($"No existe el worker de WhatsApp Web en {workerScript}.");
    }

    private static string BuildStartArguments(string sessionDir, string mode, string phone, bool includeTextCode, string instanceName)
    {
        var normalizedMode = string.Equals(mode, ConversacionWhatsAppWebSessionModes.PhoneNumber, StringComparison.OrdinalIgnoreCase)
            ? ConversacionWhatsAppWebSessionModes.PhoneNumber
            : ConversacionWhatsAppWebSessionModes.Qr;
        var normalizedPhone = (phone ?? string.Empty).Trim();
        var wantsCode = includeTextCode ? "1" : "0";
        return $"\"{WorkerScriptName}\" start \"{sessionDir}\" \"{normalizedMode}\" \"{normalizedPhone}\" \"{wantsCode}\" \"{instanceName}\"";
    }

    private static void StopProcessIfRunning(string statusFile)
    {
        if (!File.Exists(statusFile))
            return;

        try
        {
            var json = File.ReadAllText(statusFile);
            var status = JsonSerializer.Deserialize<WhatsAppWebWorkerStatus>(json, JsonOptions);
            if (status?.ProcessId is > 0)
            {
                using var process = Process.GetProcessById(status.ProcessId.Value);
                if (!process.HasExited)
                    process.Kill(true);
            }
        }
        catch
        {
            // Si el proceso ya murió o el JSON está incompleto, seguimos igual.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Si el worker todavía lo tiene tomado, el próximo refresh lo va a reemplazar.
        }
    }

    private static void PrepareSessionDirectory(string sessionDir, bool clearAuth)
    {
        Directory.CreateDirectory(sessionDir);
        RecreateDirectory(Path.Combine(sessionDir, OutboxDirName));
        RecreateDirectory(Path.Combine(sessionDir, ResultsDirName));
        RecreateDirectory(Path.Combine(sessionDir, InboxDirName));

        if (clearAuth)
            RecreateDirectory(Path.Combine(sessionDir, AuthDirName));
    }

    private static void RecreateDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Si quedó algún archivo tomado del intento anterior, el nuevo worker lo recrea igual.
        }

        Directory.CreateDirectory(path);
    }

    private static string BuildQrCodeDataUrl(string payload)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var qrData = generator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.Q);
        var svg = new QRCoder.SvgQRCode(qrData).GetGraphic(8);
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed class WhatsAppWebWorkerStatus
    {
        public string State { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? QrPayload { get; set; }
        public string? PairingCode { get; set; }
        public string Error { get; set; } = string.Empty;
        public int? ProcessId { get; set; }
        public DateTime? GeneratedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime? LastUpdatedAtUtc { get; set; }
        public string? PhoneJid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? AccountName { get; set; }
    }

    private sealed class WhatsAppWebCommandResult
    {
        public string State { get; set; } = string.Empty;
        public string? ExternalMessageId { get; set; }
        public string Error { get; set; } = string.Empty;
    }
}
