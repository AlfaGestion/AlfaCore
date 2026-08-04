using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaPedidoService
{
    Task<IReadOnlyList<PuntoVentaPedidoDto>> GetPedidosAsync(int idPuntoVenta, string? estado = null, CancellationToken ct = default);
    Task<PuntoVentaPedidoDto?> GetPedidoByIdAsync(int id, CancellationToken ct = default);
    Task<int> SavePedidoAsync(PuntoVentaPedidoSaveRequest request, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, string nuevoEstado, string? usuarioAccion = null, CancellationToken ct = default);
    Task MarcarFacturadoAsync(int idPedido, string tc, string idComprobante, string? usuarioAccion = null, CancellationToken ct = default);
    Task AnularPedidoAsync(int id, string? usuarioAccion = null, CancellationToken ct = default);
}
