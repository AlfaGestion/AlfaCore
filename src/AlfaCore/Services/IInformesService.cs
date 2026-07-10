using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IInformesService
{
    Task<InformesPageDto> GetPageAsync(string usuario, long? idArticulo = null, string? busqueda = null, bool incluirPapelera = false, CancellationToken ct = default);
    Task<InformesArticuloDetalleDto?> GetArticleAsync(long idArticulo, string usuario, CancellationToken ct = default);
    Task<long> CreateArticleAsync(InformesArticuloCreateRequest request, CancellationToken ct = default);
    Task<long> SaveArticleAsync(InformesArticuloSaveRequest request, CancellationToken ct = default);
    Task DeleteArticleAsync(long idArticulo, string usuarioAccion, CancellationToken ct = default);
    Task RestoreArticleAsync(long idArticulo, string usuarioAccion, CancellationToken ct = default);
    Task<long> DuplicateArticleAsync(long idArticulo, string usuarioAccion, CancellationToken ct = default);
    Task ToggleFavoriteAsync(long idArticulo, string usuario, bool favorito, CancellationToken ct = default);
    Task ToggleShareAsync(long idArticulo, string usuarioAccion, bool compartido, CancellationToken ct = default);
    Task AddCommentAsync(InformesComentarioRequest request, CancellationToken ct = default);
    Task ToggleCommentResolvedAsync(long idComentario, string usuarioAccion, bool resuelto, CancellationToken ct = default);
    Task RestoreVersionAsync(long idVersion, string usuarioAccion, CancellationToken ct = default);
    Task<string> SaveCoverImageAsync(Stream content, string fileName, string contentType, string usuarioAccion, CancellationToken ct = default);
}
