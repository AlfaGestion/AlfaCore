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

    /// <summary>Todas las filas del tablero ya evaluadas, de peor a mejor estado.</summary>
    Task<MonitorTableroDto> GetTableroAsync(CancellationToken ct = default);
}
