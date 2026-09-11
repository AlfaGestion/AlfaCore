using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPortalClienteInvitacionService
{
    // Envía la invitación de acceso al Portal Cliente (al cliente o, si viene IdContacto, a un
    // contacto vinculado) y devuelve el email de destino cuando el envío fue exitoso. Ante
    // cualquier condición esperada (cliente/contacto inexistente, sin email registrado, falla de
    // envío) lanza InvalidOperationException con un mensaje ya apto para mostrar al usuario.
    Task<string> EnviarInvitacionAsync(PortalClienteInvitacionRequestDto request, string usuarioEjecuta, CancellationToken ct = default);
}
