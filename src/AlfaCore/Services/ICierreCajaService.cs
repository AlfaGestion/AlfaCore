using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICierreCajaService
{
    Task<IReadOnlyList<CierreCajaOpcion>> GetUnidadesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CierreCajaOpcion>> GetCajasAsync(DateTime fecha, CancellationToken ct = default, string unidad = "");
    Task<CierreCajaPagina> ExportarSeccionAsync(CierreCajaFiltros filtros, string seccion, CancellationToken ct = default);
    Task<CierreCajaPagina> ConsultarAsync(CierreCajaFiltros filtros, string seccion, int pagina = 1, int pageSize = 50, CancellationToken ct = default);
}
