using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IInterfacesCatalogosService
{
    Task<IReadOnlyList<CatalogosModalidadOptionDto>> GetModalidadesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosListaPrecioDto>> GetListasPrecioAsync(CancellationToken ct = default);
    Task<PagedResult<CatalogosArticuloBusquedaDto>> SearchArticulosAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default);

    // Opciones reales para los combos de clasificación del picker de artículos (Rubro/Familia/Marca/Proveedor).
    Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetRubrosArticuloAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetFamiliasArticuloAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetMarcasArticuloAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetProveedoresArticuloAsync(CancellationToken ct = default);

    // Variantes sin paginar de SearchArticulosAsync (mismos filtros/exclusiones), usadas por
    // "Importar todo" (Catálogo) y "Seleccionar todos los resultados" (ArticuloPickerDialog).
    Task<int> CountArticulosAllAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> SearchArticulosAllAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default);

    Task<int> CountArticulosDesdeListaAsync(string idLista, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> GetArticulosDesdeListaAsync(string idLista, string? idWeb = null, CancellationToken ct = default);
    Task<PagedResult<CatalogosCatalogoResumenDto>> SearchCatalogosAsync(string? texto, int pageNumber = 1, int pageSize = 50, DateTime? fechaFiltro = null, string? tipoFiltro = null, string? estadoFiltro = null, CancellationToken ct = default);
    Task<CatalogosCatalogoDetalleDto?> GetCatalogoAsync(int idInsert, CancellationToken ct = default);
    Task<CatalogosCatalogoDetalleDto?> GetCatalogoPublicoAsync(int idInsert, CancellationToken ct = default);
    Task<CatalogosCatalogoSaveResultDto> SaveCatalogoVigenciaAsync(CatalogosCatalogoSaveRequestDto request, CancellationToken ct = default);
    Task<CatalogosCatalogoAccessUrlsDto> GetCatalogoAccessUrlsAsync(int idInsert, string? idWeb = null, int? idBase = null, CancellationToken ct = default);
    Task<int> GetCatalogoPredeterminadoIdAsync(CancellationToken ct = default);
    Task SetCatalogoPredeterminadoAsync(string userName, int idInsert, CancellationToken ct = default);
    Task<CatalogosClienteSessionInfo> LoginClienteAsync(CatalogosClienteLoginRequestDto request, CancellationToken ct = default);
    Task<CatalogoPedidoResultDto> ConfirmarPedidoCarritoAsync(CatalogoPedidoConfirmarRequestDto request, CancellationToken ct = default);
    Task FinalizarCatalogoAsync(int idInsert, string usuario, string pc, CancellationToken ct = default);
    Task<bool> GetMenuHabilitadoAsync(CancellationToken ct = default);
    Task SaveMenuHabilitadoAsync(string userName, bool habilitado, CancellationToken ct = default);
    Task<CatalogosPublicIdentityDto> GetPublicIdentityAsync(string? idWeb, CancellationToken ct = default);
    Task SavePublicIdentityNameAsync(string userName, string? nombreVisible, CancellationToken ct = default);
    Task SavePublicLogoFormatAsync(string userName, string? idWeb, string logoFormat, CancellationToken ct = default);
    Task<CatalogosPublicIdentityDto> SavePublicIdentityLogoAsync(string userName, string? idWeb, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task ResetPublicIdentityLogoAsync(string userName, string? idWeb, CancellationToken ct = default);
    Task<CatalogosPublicLogoServeDto?> GetPublicLogoForServeAsync(string? idWeb, CancellationToken ct = default);
    Task<string> GetPublicClasePrecioAsync(string? idWeb, CancellationToken ct = default);
    Task SavePublicClasePrecioAsync(string userName, string? idWeb, string clasePrecio, CancellationToken ct = default);
    Task<CatalogosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, CatalogosViewSettingsDto settings, CancellationToken ct = default);
}
