using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services;

/// <summary>Orquesta un cobro con Mercado Pago Point desde el Punto de Venta web. Pensado para
/// inyectarse Scoped (una instancia por circuito Blazor): CobrarAsync corre el polling completo del
/// PaymentFlowController portado -- los `await` internos (creación de la orden, cada intervalo de
/// polling) liberan el sync context del circuito, así que un click concurrente en "Cancelar" sí llega
/// a procesarse y puede llamar a SolicitarCancelacion() mientras CobrarAsync sigue esperando.</summary>
public interface IMercadoPagoPointPosService
{
    /// <summary>Corre hasta un resultado final (aprobado/cancelado/error) o hasta que el
    /// CancellationToken se cancele. onStatusChanged se invoca desde el propio hilo del circuito
    /// (vía los await del polling) con un texto para mostrar en la UI.</summary>
    Task<PaymentResult> CobrarAsync(decimal importe, string externalReference, Action<string> onStatusChanged, CancellationToken ct = default, int idPuntoVenta = 0, string? terminalId = null);

    /// <summary>Pide cancelar el cobro en curso (si hay uno). No hace nada si CobrarAsync no está
    /// corriendo en este momento para este mismo circuito.</summary>
    void SolicitarCancelacion();

    /// <summary>Vincula la orden ya aprobada (por ExternalReference) al comprobante recién grabado,
    /// para trazabilidad/reconciliación en dbo.MP_POINT_ORDENES.</summary>
    Task VincularComprobanteAsync(string externalReference, int idComprobante, CancellationToken ct = default);

    /// <summary>Llamado por el webhook: nunca confía en el body de la notificación, siempre vuelve a
    /// pedir el estado real de la orden a la API antes de actualizar dbo.MP_POINT_ORDENES.</summary>
    Task ActualizarDesdeWebhookAsync(string idOrderMp, CancellationToken ct = default);
}
