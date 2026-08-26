using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IBaseMaestraImagenService
{
    Task<BaseMaestraImagenArticuloDto> ConsultarArticuloAsync(BaseMaestraImagenOrigenDto articulo, string? idClienteFtp, int? idBase, CancellationToken ct = default, bool forceRefresh = false);
    Task<ArticuloImagenArchivoDto?> ObtenerPreviewAsync(string imageUrl, string? idClienteFtp, int? idBase, CancellationToken ct = default);
    Task<ArticuloImagenArchivoDto?> ObtenerPreviewDesdeCodigoAsync(string codigo, string? idClienteFtp, int? idBase, CancellationToken ct = default);
    Task<ArticuloImagenArchivoDto?> BuscarImagenCatalogoAsync(string idArticulo, string codigoBarra, string descripcionArticulo, CancellationToken ct = default);
    Task<BaseMaestraImagenResultadoDto> AsignarImagenesAsync(
        IReadOnlyList<BaseMaestraImagenArticuloDto> articulos,
        string? idClienteFtp,
        int? idBase,
        Action<int, int, string>? progressReporter = null,
        CancellationToken ct = default);
    string BuildPreviewUrl(string imageUrl, string? idClienteFtp, int? idBase);
    string BuildPreviewUrlFromCodigo(string codigo, string? idClienteFtp, int? idBase);
    string BuildGoogleImagesSearchUrl(string codigoBarra, string descripcionArticulo);
}
