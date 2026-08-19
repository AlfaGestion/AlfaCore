using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPortalClienteRecuperarClaveService
{
    Task<PortalClienteRecuperarClaveResultDto> SolicitarAsync(PortalClienteRecuperarClaveRequestDto request, CancellationToken ct = default);
    Task<PortalClienteValidarTokenResultDto> ValidarTokenAsync(string token, CancellationToken ct = default);
    Task<PortalClienteRestablecerClaveResultDto> RestablecerAsync(PortalClienteRestablecerClaveRequestDto request, CancellationToken ct = default);
}
