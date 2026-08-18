using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Estado del worker de lectura automática de compras, llevado POR BASE: el worker recorre todas
/// las bases activas en cada ciclo (ver <see cref="InterfacesCompraIaWorkerHostedService"/>), así
/// que "un solo estado global" no alcanza — cada cliente/base necesita ver el estado de SU propia
/// cola, no el de la última base que se haya procesado en el ciclo compartido.
/// </summary>
public sealed class InterfacesCompraIaWorkerState
{
    private readonly object _sync = new();
    private readonly Dictionary<int, BaseState> _porBase = new();

    private sealed class BaseState
    {
        public bool IsRunning;
        public DateTime? LastHeartbeatAt;
        public DateTime? LastRunStartedAt;
        public DateTime? LastRunFinishedAt;
        public DateTime? NextPlannedRunAt;
        public int LastProcessedCount;
        public string LastMessage = string.Empty;
        public string LastError = string.Empty;
    }

    private BaseState GetOrAdd(int idBase)
    {
        if (!_porBase.TryGetValue(idBase, out var estado))
        {
            estado = new BaseState();
            _porBase[idBase] = estado;
        }

        return estado;
    }

    public void MarkRunning(int idBase, string message)
    {
        lock (_sync)
        {
            var e = GetOrAdd(idBase);
            e.IsRunning = true;
            e.LastHeartbeatAt = DateTime.Now;
            e.LastRunStartedAt = DateTime.Now;
            e.LastMessage = message;
            e.LastError = string.Empty;
        }
    }

    public void MarkWaiting(int idBase, int delaySeconds, string message)
    {
        lock (_sync)
        {
            var e = GetOrAdd(idBase);
            e.IsRunning = false;
            e.LastHeartbeatAt = DateTime.Now;
            e.LastRunFinishedAt = DateTime.Now;
            e.NextPlannedRunAt = DateTime.Now.AddSeconds(Math.Max(1, delaySeconds));
            e.LastMessage = message;
        }
    }

    public void MarkWarning(int idBase, int delaySeconds, string message)
    {
        lock (_sync)
        {
            var e = GetOrAdd(idBase);
            e.IsRunning = false;
            e.LastHeartbeatAt = DateTime.Now;
            e.LastRunFinishedAt = DateTime.Now;
            e.NextPlannedRunAt = DateTime.Now.AddSeconds(Math.Max(1, delaySeconds));
            e.LastMessage = message;
            e.LastError = string.Empty;
        }
    }

    public void MarkError(int idBase, int delaySeconds, string message, string error)
    {
        lock (_sync)
        {
            var e = GetOrAdd(idBase);
            e.IsRunning = false;
            e.LastHeartbeatAt = DateTime.Now;
            e.LastRunFinishedAt = DateTime.Now;
            e.NextPlannedRunAt = DateTime.Now.AddSeconds(Math.Max(1, delaySeconds));
            e.LastMessage = message;
            e.LastError = error;
        }
    }

    public void MarkProcessed(int idBase, int processedCount)
    {
        lock (_sync)
        {
            var e = GetOrAdd(idBase);
            e.LastProcessedCount = processedCount;
            e.LastHeartbeatAt = DateTime.Now;
        }
    }

    public InterfacesCompraIaWorkerStatusDto Snapshot(int idBase, bool workerEnabled, int intervalSeconds)
    {
        lock (_sync)
        {
            if (!_porBase.TryGetValue(idBase, out var e))
            {
                return new InterfacesCompraIaWorkerStatusDto
                {
                    Estado = workerEnabled ? "ESPERANDO" : "DESHABILITADO",
                    Descripcion = workerEnabled ? "Worker todavía no ejecutó ningún ciclo para esta base" : "Worker deshabilitado",
                    ProximoIntento = null
                };
            }

            var now = DateTime.Now;
            var staleThresholdSeconds = Math.Max(15, Math.Max(3, intervalSeconds) * 2 + 5);
            var recentError = workerEnabled
                && !e.IsRunning
                && !string.IsNullOrWhiteSpace(e.LastError)
                && e.LastRunFinishedAt.HasValue
                && (now - e.LastRunFinishedAt.Value).TotalSeconds <= staleThresholdSeconds;
            var stale = workerEnabled
                && !e.IsRunning
                && e.LastHeartbeatAt.HasValue
                && (now - e.LastHeartbeatAt.Value).TotalSeconds > staleThresholdSeconds;

            var estado = !workerEnabled
                ? "DESHABILITADO"
                : e.IsRunning
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
                UltimaEjecucion = e.LastRunFinishedAt ?? e.LastHeartbeatAt,
                UltimoInicio = e.LastRunStartedAt,
                ProximoIntento = workerEnabled ? e.NextPlannedRunAt : null,
                UltimoMensaje = e.LastMessage,
                UltimoError = e.LastError,
                UltimosProcesados = e.LastProcessedCount
            };
        }
    }
}
