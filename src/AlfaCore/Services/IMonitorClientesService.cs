using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Monitor de Clientes (Administrar → Monitor de Clientes): estado del sistema legacy de cada cliente
/// (envíos y recepciones de puntos de venta, backup, tareas automáticas). El sistema del cliente manda
/// los datos (POST /api/vb6/monitor-estado) y acá se decide, con los límites de MON_CONTROL_TIPO /
/// MON_CONTROL_CLIENTE, si cada control está OK, en aviso o crítico. Las tablas viven en ALFANET2007.
/// </summary>
public interface IMonitorClientesService
{
    /// <summary>False si la instalación no tiene la conexión <c>AlfaGestion</c> (base central de ALFA NET).</summary>
    bool Disponible { get; }

    /// <summary>Graba el reporte de un sistema. Lanza <see cref="KeyNotFoundException"/> si la licencia no
    /// existe en ALFA_CENTRAL e <see cref="InvalidOperationException"/> si faltan datos.</summary>
    Task<MonitorReporteResultado> RegistrarReporteAsync(MonitorReporteRequest request, CancellationToken ct = default);

    /// <summary>Todas las filas del tablero ya evaluadas, de peor a mejor estado, con su seguimiento.</summary>
    Task<MonitorTableroDto> GetTableroAsync(CancellationToken ct = default);

    /// <summary>Clientes activos e imputables del padrón central que nunca reportaron al monitor.</summary>
    Task<IReadOnlyList<MonitorClienteSinReporteDto>> GetClientesSinReporteAsync(CancellationToken ct = default);

    /// <summary>Tomar / soltar, silenciar o anotar una fila del tablero.</summary>
    Task GuardarSeguimientoAsync(MonitorSeguimientoRequest request, CancellationToken ct = default);

    /// <summary>
    /// Las filas que volvieron a OK o que ya no existen quedan libres (sin tomar ni nota); los silencios
    /// siguen hasta que vencen. Borra los seguimientos que quedaron vacíos. Devuelve cuántos tocó.
    /// </summary>
    Task<int> LimpiarSeguimientosAsync(MonitorTableroDto tablero, CancellationToken ct = default);

    Task<MonitorAvisoConfigDto> GetAvisoConfigAsync(CancellationToken ct = default);

    /// <summary>Usuarios activos de la base central, donde se entregan los avisos push.</summary>
    Task<IReadOnlyList<string>> GetUsuariosPushAsync(string sistema, CancellationToken ct = default);

    /// <summary>Graba lo que se edita desde el tablero (activo, prioridades, repetir, usuarios push).</summary>
    Task GuardarAvisoConfigAsync(MonitorAvisoConfigDto config, string usuario, CancellationToken ct = default);

    /// <summary>Graba lo que actualiza el proceso de avisos (hilo, último envío, firma, forzar).</summary>
    Task GuardarAvisoEstadoAsync(MonitorAvisoConfigDto config, CancellationToken ct = default);

    /// <summary>Pide al proceso de avisos que mande el aviso en su próximo ciclo (cada minuto).</summary>
    Task SolicitarAvisoAhoraAsync(string usuario, CancellationToken ct = default);
}
