using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class InterfacesCompraIaWorkerState
{
    private readonly object _sync = new();

    public bool IsRunning { get; private set; }
    public DateTime? LastHeartbeatAt { get; private set; }
    public DateTime? LastRunStartedAt { get; private set; }
    public DateTime? LastRunFinishedAt { get; private set; }
    public DateTime? NextPlannedRunAt { get; private set; }
    public int LastProcessedCount { get; private set; }
    public string LastMessage { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    public void MarkRunning(string message)
    {
        lock (_sync)
        {
            IsRunning = true;
            LastHeartbeatAt = DateTime.Now;
            LastRunStartedAt = DateTime.Now;
            LastMessage = message;
            LastError = string.Empty;
        }
    }

    public void MarkWaiting(int delaySeconds, string message)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastHeartbeatAt = DateTime.Now;
            LastRunFinishedAt = DateTime.Now;
            NextPlannedRunAt = DateTime.Now.AddSeconds(Math.Max(1, delaySeconds));
            LastMessage = message;
        }
    }

    public void MarkWarning(int delaySeconds, string message)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastHeartbeatAt = DateTime.Now;
            LastRunFinishedAt = DateTime.Now;
            NextPlannedRunAt = DateTime.Now.AddSeconds(Math.Max(1, delaySeconds));
            LastMessage = message;
            LastError = string.Empty;
        }
    }

    public void MarkError(int delaySeconds, string message, string error)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastHeartbeatAt = DateTime.Now;
            LastRunFinishedAt = DateTime.Now;
            NextPlannedRunAt = DateTime.Now.AddSeconds(Math.Max(1, delaySeconds));
            LastMessage = message;
            LastError = error;
        }
    }

    public void MarkProcessed(int processedCount)
    {
        lock (_sync)
        {
            LastProcessedCount = processedCount;
            LastHeartbeatAt = DateTime.Now;
        }
    }

    public InterfacesCompraIaWorkerStatusDto Snapshot(bool workerEnabled, int intervalSeconds)
    {
        lock (_sync)
        {
            var now = DateTime.Now;
            var staleThresholdSeconds = Math.Max(15, Math.Max(3, intervalSeconds) * 2 + 5);
            var recentError = workerEnabled
                && !IsRunning
                && !string.IsNullOrWhiteSpace(LastError)
                && LastRunFinishedAt.HasValue
                && (now - LastRunFinishedAt.Value).TotalSeconds <= staleThresholdSeconds;
            var stale = workerEnabled
                && !IsRunning
                && LastHeartbeatAt.HasValue
                && (now - LastHeartbeatAt.Value).TotalSeconds > staleThresholdSeconds;

            var estado = !workerEnabled
                ? "DESHABILITADO"
                : IsRunning
                    ? "PROCESANDO"
                    : recentError
                        ? "ERROR_RECIENTE"
                    : stale
                        ? "SIN_ACTIVIDAD"
                        : "ESPERANDO";

            var descripcion = estado switch
            {
                "DESHABILITADO" => "Worker deshabilitado",
                "PROCESANDO" => "Worker procesando",
                "ERROR_RECIENTE" => "Worker con error en el último ciclo",
                "SIN_ACTIVIDAD" => "Worker sin actividad",
                _ => "Worker esperando próximo ciclo"
            };

            return new InterfacesCompraIaWorkerStatusDto
            {
                Estado = estado,
                Descripcion = descripcion,
                UltimaEjecucion = LastRunFinishedAt ?? LastHeartbeatAt,
                UltimoInicio = LastRunStartedAt,
                ProximoIntento = workerEnabled ? NextPlannedRunAt : null,
                UltimoMensaje = LastMessage,
                UltimoError = LastError,
                UltimosProcesados = LastProcessedCount
            };
        }
    }
}
