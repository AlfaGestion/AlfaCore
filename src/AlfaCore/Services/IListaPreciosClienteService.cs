using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IListaPreciosClienteService
{
    Task<ListaPreciosResultadoDto> BuscarAsync(ListaPreciosBusquedaFiltroDto filtro, CancellationToken ct = default);

    // Igual a BuscarAsync pero sin paginación (hasta un tope de seguridad), para exportar Excel/PDF
    // respetando exactamente los mismos filtros y el mismo criterio de lista/clase que la pantalla.
    Task<ListaPreciosExportDto> ObtenerParaExportarAsync(ListaPreciosBusquedaFiltroDto filtro, CancellationToken ct = default);
}
