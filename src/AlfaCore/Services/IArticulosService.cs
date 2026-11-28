using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Maestro de Artículos (Archivos > Maestros > Artículos), MVP calcado del patrón de
/// ICuentasComercialesService (mismo mecanismo de lista paginada, alta/edición, baja lógica y
/// configuración de vista por usuario). Alcance y columnas reales verificadas en
/// C:\Users\albert\.claude\plans\fluttering-drifting-moonbeam.md.
/// </summary>
public interface IArticulosService
{
    Task<PagedResult<ArticuloGridItemDto>> SearchAsync(ArticuloFilters filters, CancellationToken ct = default);
    Task<ArticuloDetailDto?> GetByIdAsync(string codigo, CancellationToken ct = default);
    Task<ArticuloLookupDataDto> GetLookupDataAsync(CancellationToken ct = default);

    /// <summary>Solapa "Listas": todas las listas de ventas, tildadas (con sus precios) las que tienen el artículo.
    /// Sin código (alta) vienen todas destildadas.</summary>
    Task<List<ArticuloListaPrecioDto>> GetListasPrecioAsync(string? codigo, CancellationToken ct = default)
        => Task.FromResult(new List<ArticuloListaPrecioDto>());

    /// <summary>Búsqueda por código o razón social contra vt_proveedores -- lista completa, no se
    /// precarga (puede ser grande), a diferencia de Rubros/Marcas/Unidades.</summary>
    Task<IReadOnlyList<ArticuloLookupOptionDto>> SearchProveedoresAsync(string texto, CancellationToken ct = default);

    /// <summary>Alta o edición. El código (IDARTICULO) es la clave de negocio -- se crea con el valor
    /// tipeado por el usuario (igual que FrmArtAlta.frm: el código escaneado/tipeado ES el artículo
    /// nuevo), y no se puede modificar una vez creado. GetNextCodigoAsync solo lo propone como punto de
    /// partida editable, no reserva nada.</summary>
    Task<string> SaveAsync(ArticuloSaveRequest request, CancellationToken ct = default);

    /// <summary>Igual que SaveAsync y además devuelve lo que se recalculó en cascada (productos que usan este
    /// artículo como insumo con "Calcular costo automáticamente").</summary>
    Task<ArticuloGuardadoResultado> GuardarConCascadaAsync(ArticuloSaveRequest request, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Simula el grabado (todo se deshace) y devuelve qué productos cambiarían en cascada, para confirmar antes.</summary>
    Task<ArticuloCascadaResultado> PrevisualizarCascadaAsync(ArticuloSaveRequest request, CancellationToken ct = default)
        => Task.FromResult(new ArticuloCascadaResultado());

    /// <summary>Próximo código numérico libre (MAX(IDARTICULO numérico) + 1), igual que la planilla
    /// legacy (Ma_art.frm) propone al dar de alta. Solo un punto de partida editable -- el usuario
    /// puede sobreescribirlo con cualquier código, existente (para editar) o no (para crear).</summary>
    Task<string> GetNextCodigoAsync(CancellationToken ct = default);

    /// <summary>Baja lógica (V_MA_ARTICULOS.SUSPENDIDO = 1). Sin borrado físico.</summary>
    Task DeactivateAsync(string codigo, CancellationToken ct = default);

    Task<ArticuloViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, ArticuloViewSettingsDto settings, CancellationToken ct = default);

    /// <summary>URL pública de la imagen del artículo (misma que ya usa el catálogo del Portal
    /// Cliente, vía FTP) -- vacía si no hay FTP_CODIGOCTA configurado o el artículo no tiene
    /// imagen todavía (el &lt;img&gt; simplemente no carga, no es un error).</summary>
    Task<string> GetImagenUrlAsync(string codigo, bool forzarRecarga = false, CancellationToken ct = default);

    /// <summary>Código de cliente FTP (ver GetImagenUrlAsync/UploadImagenAsync) expuesto para poder
    /// abrir el diálogo compartido `BaseMaestraImagenesDialog` (búsqueda automática de imágenes por
    /// código de barras/descripción) desde el editor de Artículos.</summary>
    Task<string> GetFtpCodigoCtaAsync(CancellationToken ct = default);

    /// <summary>Sube una imagen para el artículo -- misma mecánica que usa
    /// BaseMaestraImagenService.AsignarImagenesAsync para una asignación manual: se sube tal cual
    /// (sin resize) como imagen de tamaño completo y como thumbnail, y se marca
    /// V_MA_ARTICULOS.ModificoImagen='S' para que el catálogo invalide su caché.</summary>
    Task UploadImagenAsync(string codigo, byte[] contenido, string extension, CancellationToken ct = default);

    /// <summary>Solapa "Fórmula": insumos y tareas de producción del artículo (V_MA_INSUMOS / V_MA_INSUMOSTAREAS),
    /// con el valor de cada insumo ya calculado. Vacía si el artículo no tiene fórmula.</summary>
    Task<ArticuloFormulaDto> GetFormulaAsync(string codigo, CancellationToken ct = default)
        => Task.FromResult(new ArticuloFormulaDto());

    /// <summary>Completa un renglón de la fórmula (descripción, unidad, valor por unidad, error) sin grabar nada.</summary>
    Task<ArticuloFormulaInsumoDto> ResolverInsumoAsync(string? codigoProducto, ArticuloFormulaInsumoDto renglon, CancellationToken ct = default)
        => Task.FromResult(renglon);

    /// <summary>Búsqueda F5 de insumos (artículos activos); en Stock viene el costo para el buscador compartido.</summary>
    Task<IReadOnlyList<StockMovimientoArticulo>> BuscarInsumosAsync(string texto, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StockMovimientoArticulo>>([]);

    /// <summary>Familias (V_TA_FAMILIAS) para la accion por lote "Asignar familia".</summary>
    Task<IReadOnlyList<ArticuloLookupOptionDto>> GetFamiliasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ArticuloLookupOptionDto>>([]);

    /// <summary>Accion por lote sobre los articulos seleccionados: aumento %, rubro, marca, familia o
    /// utilidad. El aumento % no se permite en modo Retail (los precios de venta salen de las listas).</summary>
    Task<ArticuloLoteResultado> AplicarLoteAsync(ArticuloLoteRequest request, CancellationToken ct = default)
        => throw new NotSupportedException();
}
