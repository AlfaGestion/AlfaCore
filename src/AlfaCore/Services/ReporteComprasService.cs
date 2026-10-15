using AlfaCore.Models;
namespace AlfaCore.Services;

public sealed class ReporteComprasService(ReporteComercialService reportes) : IReporteComprasService
{
    public Task<ReporteComprasOptionsDto> GetOptionsAsync(CancellationToken ct=default) => reportes.GetOptionsAsync(false,ct);
    public Task<ResumenComprasDto> GetResumenAsync(FiltrosReporteCompras f,CancellationToken ct=default) => reportes.GetResumenAsync(false,f,ct);
    public Task<DetalleComprasResultDto> GetDetalleComprasAsync(FiltrosReporteCompras f,CancellationToken ct=default) => reportes.GetDetalleAsync(false,f,ct);
    public Task<CuentaCorrienteResultDto> GetCuentaCorrienteAsync(FiltrosReporteCompras f,CancellationToken ct=default) => reportes.GetCuentaCorrienteAsync(false,f,ct);
}
