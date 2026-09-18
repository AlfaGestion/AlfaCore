using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPortalClienteRegistrarEmailService
{
    Task<string> GenerarEnlaceAsync(PortalClienteGenerarEnlaceEmailRequestDto request, CancellationToken ct = default);
    Task<PortalClienteRegistrarEmailResultDto> RegistrarAsync(PortalClienteRegistrarEmailRequestDto request, CancellationToken ct = default);
}
