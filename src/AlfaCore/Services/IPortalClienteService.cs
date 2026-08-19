using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPortalClienteService
{
    Task<PagedResult<PortalClientePedidoResumenDto>> GetPedidosClienteAsync(PortalClientePedidosFiltroDto filtro, CancellationToken ct = default);
    Task<PortalClientePedidoDetalleDto?> GetPedidoClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default);
    Task<PortalClienteCuentaCorrienteResumenDto> GetResumenCuentaCorrienteAsync(string codigoCliente, CancellationToken ct = default);
    Task<PortalClienteCuentaCorrienteDto> GetCuentaCorrienteAsync(PortalClienteCuentaCorrienteFiltroDto filtro, CancellationToken ct = default);
    Task<PortalClienteComprobantePendienteDetalleDto?> GetComprobanteClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default);
    Task<PortalClienteMiCuentaDto?> GetMiCuentaAsync(string codigoCliente, CancellationToken ct = default);
    Task<PortalClienteActualizarEmailResultDto> ActualizarEmailAsync(PortalClienteActualizarEmailRequestDto request, CancellationToken ct = default);
    Task<PortalClienteCambiarClaveResultDto> CambiarClaveAsync(PortalClienteCambiarClaveRequestDto request, CancellationToken ct = default);
}
