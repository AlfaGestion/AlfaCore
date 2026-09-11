using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPortalClienteRecuperarClaveService
{
    Task<PortalClienteRecuperarClaveResultDto> SolicitarAsync(PortalClienteRecuperarClaveRequestDto request, CancellationToken ct = default);
    Task<PortalClienteValidarTokenResultDto> ValidarTokenAsync(string token, CancellationToken ct = default);
    Task<PortalClienteRestablecerClaveResultDto> RestablecerAsync(PortalClienteRestablecerClaveRequestDto request, CancellationToken ct = default);

    // Genera (e invalida el anterior) un token de restablecimiento para un cliente ya identificado
    // por el llamador — usado por la invitación al Portal Cliente. Mismo mecanismo de token que
    // SolicitarAsync (hash SHA-256, un solo uso, vencimiento), pero sin el paso de "buscar cliente
    // por identificador" ni el envío de email: eso lo resuelve quien invita (PortalClienteInvitacionService).
    Task<string> GenerarEnlaceInvitacionAsync(PortalClienteGenerarEnlaceRequestDto request, CancellationToken ct = default);
}
