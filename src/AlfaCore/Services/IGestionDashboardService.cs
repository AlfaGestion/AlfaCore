using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IGestionDashboardService
{
    Task<VentasFilterOptionsDto> GetVentasFilterOptionsAsync(CancellationToken ct = default);
    Task<VentasDashboardDto> GetVentasAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<ComparativoVentasComprasDto> GetComparativoAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<VentasClientesPageDto> GetVentasClientesAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<VentasRubrosPageDto> GetVentasRubrosAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<VentasFamiliasPageDto> GetVentasFamiliasAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<VentasArticulosPageDto> GetVentasArticulosAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<VentasComprobantesPageDto> GetVentasComprobantesAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<VentasComprobanteItemDto>> GetVentasComprobanteItemsAsync(string tc, string idComprobante, CancellationToken ct = default);
    Task<VentasResumenTcPageDto> GetVentasResumenPorTcAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<VentasComparativoPeriodoDto> GetVentasComparativoPeriodoAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<VentasUnidadNegocioResumenDto>> GetVentasPorUnidadNegocioAsync(VentasDashboardFilters filters, CancellationToken ct = default);
    Task<StockFilterOptionsDto> GetStockFilterOptionsAsync(CancellationToken ct = default);
    Task<StockDashboardDto> GetStockAsync(StockDashboardFilters filters, CancellationToken ct = default);
    Task<CajaBancosFilterOptionsDto> GetCajaBancosFilterOptionsAsync(CancellationToken ct = default);
    Task<CajaBancosDashboardDto> GetCajaBancosAsync(CajaBancosDashboardFilters filters, CancellationToken ct = default);
    Task<ContabilidadFilterOptionsDto> GetContabilidadFilterOptionsAsync(CancellationToken ct = default);
    Task<ContabilidadDashboardDto> GetContabilidadAsync(ContabilidadDashboardFilters filters, CancellationToken ct = default);
    Task<PosicionIvaDto> GetPosicionIvaAsync(ContabilidadDashboardFilters filters, CancellationToken ct = default);
    Task<BalanceSaldosDto> GetBalanceSaldosAsync(ContabilidadDashboardFilters filters, int nivel = 2, CancellationToken ct = default);
}
