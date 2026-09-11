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
    /// que genera -- ej. Cotizaciones (ver CotizacionDocumentService.BuildDataAsync). A diferencia
    /// de la foto de perfil no vive en el filesystem, porque el endpoint público de PDF cruza de
    /// base con ISessionService.SetWebhookOverride y un archivo local no viajaría con eso.</summary>
    Task<byte[]?> GetSignatureBytesAsync(string nombre, CancellationToken ct = default);
    Task SaveSignatureAsync(string nombre, byte[] contenido, CancellationToken ct = default);
    Task DeleteSignatureAsync(string nombre, CancellationToken ct = default);

    /// <summary>Firma + el texto a mostrar debajo de ella (nombre y apellido, no el usuario de
    /// sistema) -- usado al armar el PDF.</summary>
    Task<UsuarioFirmaDto?> GetSignatureAsync(string nombre, CancellationToken ct = default);
    Task SaveSignatureDisplayNameAsync(string nombre, string? nombreMostrar, CancellationToken ct = default);
}
