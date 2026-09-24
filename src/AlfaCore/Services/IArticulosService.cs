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

    /// <summary>Búsqueda por código o razón social contra vt_proveedores -- lista completa, no se
    /// precarga (puede ser grande), a diferencia de Rubros/Marcas/Unidades.</summary>
    Task<IReadOnlyList<ArticuloLookupOptionDto>> SearchProveedoresAsync(string texto, CancellationToken ct = default);

    /// <summary>Alta o edición. El código (IDARTICULO) es la clave de negocio -- se crea con el valor
    /// tipeado por el usuario (igual que FrmArtAlta.frm: el código escaneado/tipeado ES el artículo
    /// nuevo, no hay numeración automática separada), y no se puede modificar una vez creado.</summary>
    Task<string> SaveAsync(ArticuloSaveRequest request, CancellationToken ct = default);

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
}
