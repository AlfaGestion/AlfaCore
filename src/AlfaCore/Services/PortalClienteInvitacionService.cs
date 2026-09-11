using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Invitación al Portal Cliente desde la ficha del cliente (Clientes → Editar). No duplica
// infraestructura existente: reutiliza IPortalClienteRecuperarClaveService para el enlace de
// "crear/cambiar contraseña" (mismo token de un solo uso con vencimiento que ya usa el flujo de
// "¿Olvidaste tu contraseña?") e IPedidosEmailService para el transporte/branding del email (misma
// configuración SMTP que ya usan pedidos y recuperación de clave). Nunca envía la contraseña
// almacenada del cliente por email.
//
// Los contactos (MA_CONTACTOS) hoy NO tienen autenticación individual en el Portal Cliente: el
// login solo valida CodigoCliente/email de VT_CLIENTES contra MA_CUENTASADIC.CLAVE. Por eso, cuando
// se invita a un contacto, el email se envía a su casilla particular pero el "Usuario" indicado es
// el mismo CodigoCliente del cliente al que está vinculado (acceso compartido) — no se inventa un
// usuario ni una clave propia para el contacto.
public sealed class PortalClienteInvitacionService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IPortalClienteRecuperarClaveService recuperarClaveSvc,
    IPedidosEmailService pedidosEmailSvc) : IPortalClienteInvitacionService
{
    private const string ModuleName = "PortalClienteInvitacion";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<string> EnviarInvitacionAsync(PortalClienteInvitacionRequestDto request, string usuarioEjecuta, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "EnviarInvitacion", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var codigoCliente = (request.CodigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigoCliente))
                throw new InvalidOperationException("No se pudo identificar al cliente.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // Fuente oficial de lectura de clientes (VT_CLIENTES), igual que el resto del módulo
            // Clientes y que el propio login del Portal Cliente.
            var cliente = await cn.QuerySingleOrDefaultAsync<ClienteRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(CODIGO)), '') AS Codigo,
                    ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)), '') AS RazonSocial,
                    ISNULL(LTRIM(RTRIM(MAIL)), '') AS Email
                FROM dbo.VT_CLIENTES
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)))
                  AND ISNULL(Dada_De_Baja, 0) = 0;
                """,
                new { Codigo = codigoCliente },
                cancellationToken: token));

            if (cliente is null)
                throw new InvalidOperationException("No se encontró el cliente indicado.");

            string emailDestino;
            string nombreDestinatario;
            var esContacto = request.IdContacto is > 0;

            if (!esContacto)
            {
                emailDestino = cliente.Email;
                nombreDestinatario = cliente.RazonSocial;

                if (string.IsNullOrWhiteSpace(emailDestino))
                    throw new InvalidOperationException("El cliente no tiene un email registrado.");
            }
            else
            {
                var cuenta = cliente.Codigo.ToUpperInvariant();

                // Misma relación contacto → cuenta que ya usa CuentasComercialesService.GetContactosAsync
                // (MA_CONTACTOS_CUENTAS o MA_CONTACTOS.CuentaRel): se valida acá de nuevo para no
                // confiar en que el IdContacto recibido realmente pertenezca a este cliente.
                var contacto = await cn.QuerySingleOrDefaultAsync<ContactoRow>(new CommandDefinition(
                    """
                    SELECT TOP (1)
                        ISNULL(c.Nombre_y_Apellido, '') AS NombreApellido,
                        ISNULL(c.email, '') AS Email
                    FROM dbo.MA_CONTACTOS c
                    WHERE CONVERT(int, ISNULL(NULLIF(c.idContacto, 0), c.id)) = @IdContacto
                      AND (
                            EXISTS (
                                SELECT 1 FROM dbo.MA_CONTACTOS_CUENTAS rel
                                WHERE rel.IdContacto = ISNULL(NULLIF(c.idContacto, 0), c.id)
                                  AND UPPER(LTRIM(RTRIM(rel.Cuenta))) = @Cuenta
                            )
                            OR UPPER(LTRIM(RTRIM(ISNULL(c.CuentaRel, '')))) = @Cuenta
                          );
                    """,
                    new { IdContacto = request.IdContacto!.Value, Cuenta = cuenta },
                    cancellationToken: token));

                if (contacto is null)
                    throw new InvalidOperationException("No se encontró el contacto para este cliente.");

                emailDestino = contacto.Email;
                nombreDestinatario = contacto.NombreApellido;

                if (string.IsNullOrWhiteSpace(emailDestino))
                    throw new InvalidOperationException("Este contacto no tiene email registrado.");
            }

            var urlCambiarClave = await recuperarClaveSvc.GenerarEnlaceInvitacionAsync(
                new PortalClienteGenerarEnlaceRequestDto
                {
                    CodigoCliente = cliente.Codigo,
                    IdWeb = request.IdWeb,
                    IdBase = request.IdBase,
                    UrlBaseRestablecer = request.UrlBaseRestablecer
                },
                token);

            var enviado = await pedidosEmailSvc.EnviarInvitacionPortalAsync(
                emailDestino,
                nombreDestinatario,
                request.NombreEmpresa,
                request.LogoUrlAbsoluta,
                cliente.Codigo,
                cliente.RazonSocial,
                esContacto,
                request.UrlPortal,
                urlCambiarClave,
                token);

            if (!enviado)
                throw new InvalidOperationException("No pudimos enviar la invitación. Intentá nuevamente en unos minutos.");

            // Auditoría: código de cliente, contacto (si aplica), email destino, usuario de AlfaCore
            // que ejecutó el envío y resultado — nunca la clave ni el token completo.
            await appEvents.LogAuditAsync(
                ModuleName,
                "EnviarInvitacion",
                esContacto ? "MA_CONTACTOS" : "MA_CUENTASADIC",
                cliente.Codigo,
                esContacto
                    ? "Se envió una invitación al Portal Cliente a un contacto vinculado."
                    : "Se envió una invitación al Portal Cliente al cliente.",
                new
                {
                    CodigoCliente = cliente.Codigo,
                    IdContacto = request.IdContacto,
                    EmailDestino = emailDestino,
                    UsuarioEjecuta = usuarioEjecuta
                },
                token);

            return emailDestino;
        }, "No pudimos enviar la invitación al portal.", ct);

    private sealed class ClienteRow
    {
        public string Codigo { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class ContactoRow
    {
        public string NombreApellido { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }
}
