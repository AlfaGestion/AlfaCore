using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICarritoComprasService
{
    Task<PagedResult<CarritoComprasResumenDto>> SearchAsync(CarritoComprasFiltroDto filtro, CancellationToken ct = default);
    Task<CarritoComprasGeneralDetalleDto?> GetGeneralAsync(int idCarrito, CancellationToken ct = default);
    Task<int> SaveGeneralAsync(CarritoComprasGeneralSaveRequestDto request, CancellationToken ct = default);
    Task<bool> ToggleCatalogoAsync(int idCatalogo, bool activo, CancellationToken ct = default);
    Task<bool> ToggleGeneralAsync(int idCarrito, bool activo, CancellationToken ct = default);
    Task<CarritoComprasResumenDto?> ResolvePortalCarritoAsync(string? idWeb, CancellationToken ct = default);
    Task<CatalogosCatalogoDetalleDto?> GetPublicCartAsync(int idInsert, string? idWeb = null, string? codigoCliente = null, CancellationToken ct = default);
    Task<CatalogoPedidoResultDto> ConfirmarPedidoPublicoAsync(CatalogoPedidoConfirmarRequestDto request, CancellationToken ct = default);
    Task<CarritoComprasPrecioClienteDto> ResolvePrecioClienteAsync(string? codigoCliente, CancellationToken ct = default);
}
