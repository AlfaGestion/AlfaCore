using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Informes mensuales de atención de Conversaciones: genera (y persiste) un resumen por cliente
/// del mes elegido, con totales de conversaciones/mensajes/días y minutos de partes de horas.
/// </summary>
public interface IConversacionesInformesService
{
    /// <summary>Genera y guarda el informe del período (reemplaza si ya existía uno para ese mes).</summary>
    Task<ConversacionInformeMensualDto> GenerarAsync(int anio, int mes, string? usuario, CancellationToken ct = default);

    /// <summary>Lista los informes ya generados, del más nuevo al más viejo.</summary>
    Task<IReadOnlyList<ConversacionInformeListItemDto>> ListarAsync(CancellationToken ct = default);

    /// <summary>Carga un informe con sus filas por Id.</summary>
    Task<ConversacionInformeMensualDto?> GetAsync(int idInforme, CancellationToken ct = default);

    /// <summary>Carga el informe de un período (Año/Mes) con sus filas, si existe.</summary>
    Task<ConversacionInformeMensualDto?> GetByPeriodoAsync(int anio, int mes, CancellationToken ct = default);
}
