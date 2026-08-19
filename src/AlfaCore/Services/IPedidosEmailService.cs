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
}
