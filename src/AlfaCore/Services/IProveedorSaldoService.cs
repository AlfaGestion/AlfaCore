using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IProveedorSaldoService
{
    Task<ProveedorSaldoResumenDto> GetResumenSaldoAsync(string codigoProveedor, CancellationToken ct = default);
    Task<IReadOnlyList<ProveedorComprobantePendienteDto>> GetComprobantesPendientesAsync(string codigoProveedor, CancellationToken ct = default);
}
