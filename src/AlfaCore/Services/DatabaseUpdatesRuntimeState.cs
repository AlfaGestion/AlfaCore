using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class DatabaseUpdatesRuntimeState
{
    private readonly object _lock = new();
    private ActualizacionesMotorEstadoDto _snapshot = new()
    {
        Estado = "Sin registro",
        Mensaje = "Todavía no se ejecutó ninguna corrida del motor de actualizaciones en esta instancia.",
        TieneRegistro = false
    };

    public ActualizacionesMotorEstadoDto GetSnapshot()
    {
        lock (_lock)
        {
            return Clone(_snapshot);
        }
    }

    public void MarkStarted(SessionDto? session, ActualizacionesRunRequest request)
    {
        lock (_lock)
        {
            _snapshot = new ActualizacionesMotorEstadoDto
            {
                TieneRegistro = true,
                EnEjecucion = true,
                EsEjecucionAutomatica = request.EsEjecucionAutomatica,
                UltimaEjecucionOk = false,
                Estado = request.EsEjecucionAutomatica ? "Ejecutando al iniciar" : "Ejecutando manualmente",
                Mensaje = request.EsEjecucionAutomatica
                    ? "El motor está revisando actualizaciones pendientes durante el arranque."
                    : "Se está ejecutando una corrida manual de actualizaciones.",
                InicioUltimaEjecucion = DateTime.Now,
                FinUltimaEjecucion = null,
                BaseObjetivo = BuildBaseLabel(session),
                UsuarioAccion = string.IsNullOrWhiteSpace(request.UsuarioAccion) ? "SYSTEM" : request.UsuarioAccion.Trim(),
                PcAccion = string.IsNullOrWhiteSpace(request.PcAccion) ? Environment.MachineName : request.PcAccion.Trim(),
                ScriptsAplicados = []
            };
        }
    }

    public void MarkCompleted(SessionDto? session, ActualizacionesRunRequest request, ActualizacionesRunResultDto result)
    {
        lock (_lock)
        {
            _snapshot = new ActualizacionesMotorEstadoDto
            {
                TieneRegistro = true,
                EnEjecucion = false,
                EsEjecucionAutomatica = request.EsEjecucionAutomatica,
                UltimaEjecucionOk = true,
                SinCambios = result.SinCambios,
                Estado = result.SinCambios ? "Sin cambios" : "Actualizaciones aplicadas",
                Mensaje = result.SinCambios
                    ? "No había scripts pendientes para la base activa."
                    : $"Se aplicaron {result.CantidadAplicada} actualización(es) correctamente.",
                InicioUltimaEjecucion = _snapshot.InicioUltimaEjecucion ?? DateTime.Now,
                FinUltimaEjecucion = DateTime.Now,
                BaseObjetivo = BuildBaseLabel(session),
                UsuarioAccion = string.IsNullOrWhiteSpace(request.UsuarioAccion) ? "SYSTEM" : request.UsuarioAccion.Trim(),
                PcAccion = string.IsNullOrWhiteSpace(request.PcAccion) ? Environment.MachineName : request.PcAccion.Trim(),
                VersionFinal = result.VersionFinal,
                RutaOrigen = result.RutaOrigen,
                CantidadAplicada = result.CantidadAplicada,
                ScriptsAplicados = result.ScriptsAplicados.ToArray()
            };
        }
    }

    public void MarkFailed(SessionDto? session, ActualizacionesRunRequest request, Exception ex)
    {
        lock (_lock)
        {
            _snapshot = new ActualizacionesMotorEstadoDto
            {
                TieneRegistro = true,
                EnEjecucion = false,
                EsEjecucionAutomatica = request.EsEjecucionAutomatica,
                UltimaEjecucionOk = false,
                Estado = request.EsEjecucionAutomatica ? "Error en arranque" : "Error en ejecución manual",
                Mensaje = request.EsEjecucionAutomatica
                    ? "La ejecución automática del arranque terminó con error."
                    : "La corrida manual terminó con error.",
                ErrorDetalle = ex.Message,
                InicioUltimaEjecucion = _snapshot.InicioUltimaEjecucion ?? DateTime.Now,
                FinUltimaEjecucion = DateTime.Now,
                BaseObjetivo = BuildBaseLabel(session),
                UsuarioAccion = string.IsNullOrWhiteSpace(request.UsuarioAccion) ? "SYSTEM" : request.UsuarioAccion.Trim(),
                PcAccion = string.IsNullOrWhiteSpace(request.PcAccion) ? Environment.MachineName : request.PcAccion.Trim(),
                ScriptsAplicados = []
            };
        }
    }

    private static string BuildBaseLabel(SessionDto? session)
    {
        if (session is null)
            return "Sin base resuelta";

        return string.IsNullOrWhiteSpace(session.Nombre)
            ? $"{session.Servidor} · {session.BaseDatos}"
            : $"{session.Nombre} · {session.Servidor} · {session.BaseDatos}";
    }

    private static ActualizacionesMotorEstadoDto Clone(ActualizacionesMotorEstadoDto source)
        => new()
        {
            EnEjecucion = source.EnEjecucion,
            EsEjecucionAutomatica = source.EsEjecucionAutomatica,
            TieneRegistro = source.TieneRegistro,
            UltimaEjecucionOk = source.UltimaEjecucionOk,
            SinCambios = source.SinCambios,
            InicioUltimaEjecucion = source.InicioUltimaEjecucion,
            FinUltimaEjecucion = source.FinUltimaEjecucion,
            Estado = source.Estado,
            Mensaje = source.Mensaje,
            ErrorDetalle = source.ErrorDetalle,
            BaseObjetivo = source.BaseObjetivo,
            UsuarioAccion = source.UsuarioAccion,
            PcAccion = source.PcAccion,
            VersionFinal = source.VersionFinal,
            RutaOrigen = source.RutaOrigen,
            CantidadAplicada = source.CantidadAplicada,
            ScriptsAplicados = source.ScriptsAplicados.ToArray()
        };
}
