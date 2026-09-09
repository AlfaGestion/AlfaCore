using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IUsuariosService
{
    Task<PagedResult<UsuarioGridItemDto>> SearchAsync(UsuariosFilters filters, CancellationToken ct = default);
    Task<UsuarioDetailDto?> GetByIdAsync(string nombre, CancellationToken ct = default);
    Task<string> SaveAsync(UsuarioSaveRequest request, CancellationToken ct = default);
    Task DeactivateAsync(string nombre, CancellationToken ct = default);
    Task<UsuarioPhotoServeDto?> GetPhotoForServeAsync(string nombre, CancellationToken ct = default);
    Task<UsuariosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, UsuariosViewSettingsDto settings, CancellationToken ct = default);

    /// <summary>Firma digitalizada del usuario (blob en MA_FIRMAS_USUARIO), usada al pie de los PDF
    /// que genera -- ej. Cotizaciones. A diferencia de la foto de perfil no vive en el filesystem
    /// (ver comentario en CotizacionesService.LoadFirmaParaPdfAsync).</summary>
    Task<byte[]?> GetSignatureBytesAsync(string nombre, CancellationToken ct = default);
    Task SaveSignatureAsync(string nombre, byte[] contenido, CancellationToken ct = default);
    Task DeleteSignatureAsync(string nombre, CancellationToken ct = default);
}
