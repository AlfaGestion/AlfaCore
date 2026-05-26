using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAutorizacionTareasService
{
    Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosAsync(string sistemaPermisos, CancellationToken ct = default);
    Task<AutorizacionTareasDto> GetAutorizacionAsync(string menuSistema, string sistemaPermisos, string usuario, CancellationToken ct = default);
    Task GuardarAutorizacionAsync(GuardarAutorizacionTareasRequest request, CancellationToken ct = default);
}
