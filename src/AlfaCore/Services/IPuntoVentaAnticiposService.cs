using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaAnticiposService
{
    Task<PuntoVentaNpPaginaDto> BuscarNotasPedidoAsync(PuntoVentaNpListadoFiltro filtro, CancellationToken ct = default);
    Task<PuntoVentaNpAnticiposDto?> GetNotaPedidoAsync(string idComprobante, string? cuenta = null, CancellationToken ct = default);
}
