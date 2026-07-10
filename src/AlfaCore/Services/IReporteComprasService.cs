using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IReporteComprasService
{
    Task<ReporteComprasOptionsDto> GetOptionsAsync(CancellationToken ct = default);
    Task<ResumenComprasDto> GetResumenAsync(FiltrosReporteCompras filtros, CancellationToken ct = default);
    Task<DetalleComprasResultDto> GetDetalleComprasAsync(FiltrosReporteCompras filtros, CancellationToken ct = default);
    Task<CuentaCorrienteResultDto> GetCuentaCorrienteAsync(FiltrosReporteCompras filtros, CancellationToken ct = default);
}
