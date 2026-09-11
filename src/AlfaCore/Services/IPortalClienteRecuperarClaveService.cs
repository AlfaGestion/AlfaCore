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

    // Envía el email de invitación al Portal Cliente usando exactamente el mismo transporte SMTP
    // que el flujo real de "¿Olvidaste tu contraseña?" (configuración RegistroPublico:Email* del
    // servidor) — no la cuenta de TA_CONFIGURACION que usa PedidosEmailService para confirmaciones
    // de pedido, que es una cuenta distinta y puede no estar operativa.
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
