using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPedidosEmailService
{
    Task<CatalogoPedidoEmailResultDto> EnviarConfirmacionPedidoAsync(CatalogoPedidoEmailRequestDto request, CancellationToken ct = default);

    // Reutiliza la misma configuración SMTP (TA_CONFIGURACION EMAIL_*) que el envío de pedidos;
    // no crea una configuración de correo separada.
    Task<bool> EnviarRecuperacionClaveAsync(
        string emailDestino,
        string nombreCliente,
        string nombreEmpresa,
        string? logoUrlAbsoluta,
        string urlRestablecer,
        CancellationToken ct = default);

    // Invitación de acceso al Portal Cliente (ficha del cliente en AlfaCore, tanto para el cliente
    // como para un contacto vinculado). Reutiliza la misma configuración/cuentas SMTP que el resto
    // de esta clase; solo agrega el armado del email de invitación.
    Task<bool> EnviarInvitacionPortalAsync(
        string emailDestino,
        string nombreDestinatario,
        string nombreEmpresa,
        string? logoUrlAbsoluta,
        string codigoCliente,
        string razonSocialCliente,
        bool esContacto,
        string urlPortal,
        string urlCambiarClave,
        CancellationToken ct = default);
}
