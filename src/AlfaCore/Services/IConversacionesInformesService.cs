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

    /// <summary>Detalle de una fila: sus datos, el email del cliente y las conversaciones del mes.</summary>
    Task<ConversacionInformeDetalleDto?> GetDetalleAsync(int idDetalle, CancellationToken ct = default);

    /// <summary>Genera con IA el resumen (cara al cliente) de las conversaciones del mes y lo guarda como borrador.</summary>
    Task<string> GenerarResumenAsync(int idDetalle, CancellationToken ct = default);

    /// <summary>Guarda el texto del resumen editado por el operador.</summary>
    Task GuardarResumenEditadoAsync(int idDetalle, string texto, CancellationToken ct = default);

    /// <summary>Envía el resumen por email al destinatario y marca la fila como enviada.</summary>
    Task EnviarPorEmailAsync(int idDetalle, string destinatario, CancellationToken ct = default);

    /// <summary>Envía el resumen por WhatsApp a la conversación del cliente (si la ventana está abierta).</summary>
    Task EnviarPorWhatsAppAsync(int idDetalle, CancellationToken ct = default);

    /// <summary>Cantidad de mensajes por mes en los últimos 12 meses (terminando en Año/Mes), para ver la tendencia.</summary>
    Task<IReadOnlyList<ConversacionInformeTendenciaDto>> GetTendenciaMensajesAsync(int anio, int mes, CancellationToken ct = default);
}
