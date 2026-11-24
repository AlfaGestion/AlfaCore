using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Operacion del modo Salon del POS: plano de mesas, apertura, carga de items por rondas,
/// envio a cocina/barra, cuenta, cambio y union de mesas. El cobro NO esta aca: se hace
/// en Mostrador (?idPedido=X&amp;accionPedido=cerrar) con PuntoVentaService.CreateSaleAsync,
/// igual que Delivery.
/// </summary>
public interface IPuntoVentaSalonService
{
    Task<PuntoVentaSalonDto> GetSalonAsync(int idPuntoVenta, bool incluirInactivas = false, CancellationToken ct = default);
    Task<PuntoVentaSalonMesaDetalleDto?> GetMesaDetalleAsync(int idPedido, CancellationToken ct = default);
    Task<PuntoVentaSalonAbrirMesaResult> AbrirMesaAsync(PuntoVentaSalonAbrirMesaRequest request, CancellationToken ct = default);
    Task ActualizarMesaAsync(int idPedido, string mozo, int? cubiertos, string observaciones, string? usuarioAccion = null, CancellationToken ct = default);

    /// <summary>Agrega una ronda de items (quedan PENDIENTE; si <paramref name="enviar"/> se envian en el acto).</summary>
    Task AgregarItemsAsync(int idPedido, IReadOnlyList<PuntoVentaPedidoItemDto> items, bool enviar, string origen, string? usuarioAccion = null, CancellationToken ct = default);

    /// <summary>Pasa a ENVIADO las lineas PENDIENTE con destino; las sin destino quedan SERVIDO. Devuelve cuantas se enviaron a preparar.</summary>
    Task<int> EnviarPendientesAsync(int idPedido, string? usuarioAccion = null, CancellationToken ct = default);

    Task AnularLineaAsync(int idLinea, string motivo, string? usuarioAccion = null, CancellationToken ct = default);
    Task CambiarEstadoLineaAsync(int idLinea, string estado, string? usuarioAccion = null, CancellationToken ct = default);

    Task PedirCuentaAsync(int idPedido, string? usuarioAccion = null, CancellationToken ct = default);
    Task ReabrirAsync(int idPedido, string? usuarioAccion = null, CancellationToken ct = default);
    Task CambiarMesaAsync(int idPedido, int idMesaDestino, string? usuarioAccion = null, CancellationToken ct = default);
    Task UnirMesasAsync(int idPedidoOrigen, int idPedidoDestino, string? usuarioAccion = null, CancellationToken ct = default);
    Task AnularMesaAsync(int idPedido, string motivo, string? usuarioAccion = null, CancellationToken ct = default);

    Task AtenderLlamadoAsync(int idLlamado, string? usuarioAccion = null, CancellationToken ct = default);

    Task GuardarPosicionesAsync(IReadOnlyList<PuntoVentaMesaDto> mesas, CancellationToken ct = default);
}
