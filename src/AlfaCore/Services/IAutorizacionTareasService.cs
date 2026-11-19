using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAutorizacionTareasService
{
    Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosAsync(string sistemaPermisos, CancellationToken ct = default);
    Task<AutorizacionTareasDto> GetAutorizacionAsync(string menuSistema, string sistemaPermisos, string usuario, CancellationToken ct = default);
    Task GuardarAutorizacionAsync(GuardarAutorizacionTareasRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PerfilWebResumenDto>> GetPerfilesAsync(string sistemaPermisos, CancellationToken ct = default);
    Task<PerfilWebArbolDto> GetPerfilArbolAsync(string menuSistema, string sistemaPermisos, int? idPerfil, CancellationToken ct = default);
    Task<int> GuardarPerfilAsync(GuardarPerfilWebRequest request, CancellationToken ct = default);
    Task EliminarPerfilAsync(string menuSistema, string sistemaPermisos, int idPerfil, CancellationToken ct = default);
}
