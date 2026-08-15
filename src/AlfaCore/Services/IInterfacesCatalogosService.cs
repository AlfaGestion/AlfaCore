using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IInterfacesCatalogosService
{
    Task<IReadOnlyList<CatalogosModalidadOptionDto>> GetModalidadesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosListaPrecioDto>> GetListasPrecioAsync(CancellationToken ct = default);
    Task<PagedResult<CatalogosArticuloBusquedaDto>> SearchArticulosAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default);
    Task<int> CountArticulosDesdeListaAsync(string idLista, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> GetArticulosDesdeListaAsync(string idLista, CancellationToken ct = default);
    Task<PagedResult<CatalogosCatalogoResumenDto>> SearchCatalogosAsync(string? texto, int pageNumber = 1, int pageSize = 50, DateTime? fechaFiltro = null, string? tipoFiltro = null, string? estadoFiltro = null, CancellationToken ct = default);
    Task<CatalogosCatalogoDetalleDto?> GetCatalogoAsync(int idInsert, CancellationToken ct = default);
    Task<CatalogosCatalogoDetalleDto?> GetCatalogoPublicoAsync(int idInsert, CancellationToken ct = default);
    Task<CatalogosCatalogoSaveResultDto> SaveCatalogoVigenciaAsync(CatalogosCatalogoSaveRequestDto request, CancellationToken ct = default);
    Task<CatalogosCatalogoAccessUrlsDto> GetCatalogoAccessUrlsAsync(int idInsert, string? idWeb = null, int? idBase = null, CancellationToken ct = default);
    Task<CatalogosClienteSessionInfo> LoginClienteAsync(CatalogosClienteLoginRequestDto request, CancellationToken ct = default);
    Task FinalizarCatalogoAsync(int idInsert, string usuario, string pc, CancellationToken ct = default);
    Task<bool> GetMenuHabilitadoAsync(CancellationToken ct = default);
    Task SaveMenuHabilitadoAsync(string userName, bool habilitado, CancellationToken ct = default);
    Task<CatalogosPublicIdentityDto> GetPublicIdentityAsync(string? idWeb, CancellationToken ct = default);
    Task SavePublicIdentityNameAsync(string userName, string? nombreVisible, CancellationToken ct = default);
    Task SavePublicLogoFormatAsync(string userName, string? idWeb, string logoFormat, CancellationToken ct = default);
    Task<CatalogosPublicIdentityDto> SavePublicIdentityLogoAsync(string userName, string? idWeb, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task ResetPublicIdentityLogoAsync(string userName, string? idWeb, CancellationToken ct = default);
    Task<CatalogosPublicLogoServeDto?> GetPublicLogoForServeAsync(string? idWeb, CancellationToken ct = default);
    Task<CatalogosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, CatalogosViewSettingsDto settings, CancellationToken ct = default);
}
