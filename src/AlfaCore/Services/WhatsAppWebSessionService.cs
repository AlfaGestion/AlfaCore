using AlfaCore.Models;
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
    private const string OutboxDirName = "outbox";
    private const string ResultsDirName = "results";

    public async Task<ConversacionWhatsAppConfigDto> StartSessionAsync(bool includeTextCode, CancellationToken ct = default)
    {
        var config = await configService.GetWhatsAppConfigAsync(ct);
        EnsureWorkerFilesExist();

        if (string.Equals(config.WebSessionMode, ConversacionWhatsAppWebSessionModes.PhoneNumber, StringComparison.OrdinalIgnoreCase)
            && includeTextCode
            && string.IsNullOrWhiteSpace(config.WebPhoneNumber))
        {
            throw new InvalidOperationException("Para iniciar por número tenés que cargar primero el teléfono de la sesión Web.");
        }

        if (string.IsNullOrWhiteSpace(config.WebInstanceName))
            config.WebInstanceName = $"waweb-{Guid.NewGuid():N}"[..14];

        var sessionDir = EnsureSessionDirectory(config.WebInstanceName);
        var statusFile = Path.Combine(sessionDir, StatusFileName);

        TryDeleteFile(statusFile);
        StopProcessIfRunning(statusFile);

        var args = BuildStartArguments(sessionDir, config.WebSessionMode, config.WebPhoneNumber, includeTextCode, config.WebInstanceName);
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = args,
            WorkingDirectory = GetWorkerDirectory(),
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(startInfo);

        var updated = await WaitForStatusAndPersistAsync(config, statusFile, requireInteractiveArtifacts: true, ct);

        await appEvents.LogAuditAsync(
            "Conversaciones",
            "StartWhatsAppWebSession",
            "TA_CONFIGURACION",
            updated.WebInstanceName,
            "Sesión de WhatsApp Web iniciada.",
            new { updated.WebInstanceName, updated.WebSessionMode, includeTextCode, updated.WebRuntimeState },
            ct);

        return updated;
    }

    public async Task<ConversacionWhatsAppConfigDto> RefreshSessionAsync(CancellationToken ct = default)
    {
        var config = await configService.GetWhatsAppConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(config.WebInstanceName))
            return config;

        var statusFile = Path.Combine(EnsureSessionDirectory(config.WebInstanceName), StatusFileName);
        return await LoadStatusAndPersistAsync(config, statusFile, ct);
    }

    public async Task<ConversacionWhatsAppConfigDto> StopSessionAsync(CancellationToken ct = default)
    {
        var config = await configService.GetWhatsAppConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(config.WebInstanceName))
            return config;

        var sessionDir = EnsureSessionDirectory(config.WebInstanceName);
        var statusFile = Path.Combine(sessionDir, StatusFileName);
        StopProcessIfRunning(statusFile);

        config.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.Disconnected;
        config.WebRuntimeState = "STOPPED";
        config.WebLastError = string.Empty;
        config.WebWorkerProcessId = null;
        config.WebRuntimeUpdatedAtUtc = DateTime.UtcNow;
        config.WebPairingCode = string.Empty;
        config.WebPairingQrPayload = string.Empty;
        config.WebPairingQrDataUrl = string.Empty;
        config.WebPairingGeneratedAtUtc = null;
        config.WebPairingExpiresAtUtc = null;
        config.WebPairingToken = string.Empty;

        await configService.SaveWhatsAppConfigAsync(config, ct);

        await appEvents.LogAuditAsync(
            "Conversaciones",
            "StopWhatsAppWebSession",
            "TA_CONFIGURACION",
            config.WebInstanceName,
            "Sesión de WhatsApp Web detenida.",
            new { config.WebInstanceName },
            ct);

        return config;
    }

    public async Task<ConversacionWhatsAppWebSendResultDto> SendTextAsync(string phone, string text, string? replyToMessageId, CancellationToken ct = default)
    {
        var config = await configService.GetWhatsAppConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(config.WebInstanceName))
            throw new InvalidOperationException("No hay una instancia de WhatsApp Web configurada.");

        var sessionDir = EnsureSessionDirectory(config.WebInstanceName);
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

    private async Task<ConversacionWhatsAppConfigDto> WaitForStatusAndPersistAsync(
        ConversacionWhatsAppConfigDto config,
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
                var updated = await LoadStatusAndPersistAsync(config, statusFile, ct);
                var hasInteractiveArtifacts = updated.HasWebPairingQr || updated.HasWebPairingCode || updated.IsWebSessionReady;
                if (!requireInteractiveArtifacts || hasInteractiveArtifacts)
                    return updated;
            }

            await Task.Delay(500, ct);
        }

        throw new InvalidOperationException("No se recibió el estado inicial de WhatsApp Web a tiempo. Revisá que Node y las dependencias del worker estén instaladas correctamente.");
    }

    private async Task<ConversacionWhatsAppConfigDto> LoadStatusAndPersistAsync(ConversacionWhatsAppConfigDto config, string statusFile, CancellationToken ct)
    {
        if (!File.Exists(statusFile))
            return config;

        var json = await File.ReadAllTextAsync(statusFile, ct);
        var status = JsonSerializer.Deserialize<WhatsAppWebWorkerStatus>(json, JsonOptions) ?? new WhatsAppWebWorkerStatus();
        ApplyStatus(config, status);
        await configService.SaveWhatsAppConfigAsync(config, ct);
        return config;
    }

    private static void ApplyStatus(ConversacionWhatsAppConfigDto config, WhatsAppWebWorkerStatus status)
    {
        config.WebRuntimeState = status.State;
        config.WebLastError = status.Error;
        config.WebWorkerProcessId = status.ProcessId;
        config.WebRuntimeUpdatedAtUtc = status.LastUpdatedAtUtc;

        if (string.Equals(status.State, "CONNECTED", StringComparison.OrdinalIgnoreCase))
            config.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.Connected;
        else if (string.Equals(status.State, "QR_READY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.State, "PAIRING_CODE_READY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.State, "STARTING", StringComparison.OrdinalIgnoreCase))
            config.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.PendingQr;
        else if (!string.IsNullOrWhiteSpace(status.State))
            config.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.Disconnected;

        config.WebPairingQrPayload = status.QrPayload ?? string.Empty;
        config.WebPairingCode = status.PairingCode ?? string.Empty;
        config.WebPairingGeneratedAtUtc = status.GeneratedAtUtc;
        config.WebPairingExpiresAtUtc = status.ExpiresAtUtc;
        config.WebPairingToken = status.SessionId ?? string.Empty;
        config.WebPairingQrDataUrl = string.IsNullOrWhiteSpace(status.QrPayload)
            ? string.Empty
            : BuildQrCodeDataUrl(status.QrPayload);
    }

    private string EnsureSessionDirectory(string instanceName)
    {
        var baseId = sessionService.GetActiveSession()?.BaseId ?? 0;
        var path = Path.Combine(environment.ContentRootPath, SessionsRootDir, baseId.ToString(CultureInfo.InvariantCulture), instanceName);
        Directory.CreateDirectory(path);
        return path;
    }

    private string GetWorkerDirectory()
        => Path.Combine(environment.ContentRootPath, WorkerRelativeDir);

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
    }

    private sealed class WhatsAppWebCommandResult
    {
        public string State { get; set; } = string.Empty;
        public string? ExternalMessageId { get; set; }
        public string Error { get; set; } = string.Empty;
    }
}
