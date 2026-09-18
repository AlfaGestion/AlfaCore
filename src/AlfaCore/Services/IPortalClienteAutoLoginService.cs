using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPortalClienteAutoLoginService
{
    Task<string> GenerarEnlaceAsync(PortalClienteGenerarAutoLoginRequestDto request, CancellationToken ct = default);
    Task<PortalClienteAutoLoginResultDto> ConsumirAsync(string token, string idWeb, int idBase, CancellationToken ct = default);
}
