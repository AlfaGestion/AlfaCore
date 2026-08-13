using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IInterfacesCatalogosService
{
    Task<IReadOnlyList<CatalogosModalidadOptionDto>> GetModalidadesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosListaPrecioDto>> GetListasPrecioAsync(CancellationToken ct = default);
    Task<PagedResult<CatalogosArticuloBusquedaDto>> SearchArticulosAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default);
    Task<int> CountArticulosDesdeListaAsync(string idLista, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> GetArticulosDesdeListaAsync(string idLista, CancellationToken ct = default);
    Task<PagedResult<CatalogosCatalogoResumenDto>> SearchCatalogosAsync(string? texto, int pageNumber = 1, int pageSize = 50, DateTime? fechaFiltro = null, CancellationToken ct = default);
    Task<CatalogosCatalogoDetalleDto?> GetCatalogoAsync(int idInsert, CancellationToken ct = default);
    Task<CatalogosCatalogoDetalleDto?> GetCatalogoPublicoAsync(int idInsert, CancellationToken ct = default);
    Task<CatalogosCatalogoSaveResultDto> SaveCatalogoVigenciaAsync(CatalogosCatalogoSaveRequestDto request, CancellationToken ct = default);
    Task FinalizarCatalogoAsync(int idInsert, string usuario, string pc, CancellationToken ct = default);
    Task<CatalogosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, CatalogosViewSettingsDto settings, CancellationToken ct = default);
}
