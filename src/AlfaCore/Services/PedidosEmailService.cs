using System.Net;
using System.Net.Mail;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Envío del email de confirmación de un pedido web (Catálogos -> Carrito -> NP).
// Reutiliza el mismo criterio SMTP que ya usan CrmCotizacionService/ConversacionesInformesService:
// configuración por TA_CONFIGURACION (EMAIL_SERVER/EMAIL_PORT/EMAIL_CTA/EMAIL_PASS/EMAIL_SSL) con
// fallback a appsettings, y System.Net.Mail para el envío. No se inventa una arquitectura nueva.
public sealed class PedidosEmailService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IPedidosEmailService
{
    private const string ModuleName = "CatalogosPedidoEmail";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<CatalogoPedidoEmailResultDto> EnviarConfirmacionPedidoAsync(CatalogoPedidoEmailRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var to = (request.EmailDestino ?? string.Empty).Trim();
        if (to.Length == 0)
            return new CatalogoPedidoEmailResultDto { Enviado = false, MensajeError = "No se indicó un email de destino." };

        try
        {
            _ = new MailAddress(to);
        }
        catch (FormatException)
        {
            return new CatalogoPedidoEmailResultDto { Enviado = false, MensajeError = "El email indicado no tiene un formato válido." };
        }

        IReadOnlyList<MailInfo> cuentas;
        string html;
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            cuentas = await ResolveMailAccountsAsync(cn, ct);
            html = BuildPedidoHtml(request);
        }
        catch (InvalidOperationException ex)
        {
            await appEvents.LogErrorAsync(
                ModuleName, "EnviarConfirmacionPedido", ex, ex.Message,
                new { request.Pedido.IdComprobanteTexto, Destinatario = to },
                AppEventSeverity.Warning, ct);
            return new CatalogoPedidoEmailResultDto { Enviado = false, MensajeError = ex.Message };
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                ModuleName, "EnviarConfirmacionPedido", ex, "Fallo el envío de email de confirmación de pedido.",
                new { request.Pedido.IdComprobanteTexto, Destinatario = to },
                AppEventSeverity.Error, ct);
            return new CatalogoPedidoEmailResultDto { Enviado = false, MensajeError = "No pudimos enviar el email de confirmación. Podés reintentar el envío más tarde." };
        }

        // Intento con la cuenta principal y, si falla, con la/s de respaldo (mismo criterio que
        // sendEmailWithFallback en ReservasLaBarca): cada falla se loguea y se pasa a la siguiente.
        Exception? ultimoError = null;
        foreach (var cuenta in cuentas)
        {
            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(cuenta.From, string.IsNullOrWhiteSpace(request.NombreEmpresa) ? cuenta.From : request.NombreEmpresa.Trim()),
                    Subject = BuildSubject(request),
                    Body = html,
                    IsBodyHtml = true
                };
                message.To.Add(to);

                using var client = new SmtpClient(cuenta.Server, cuenta.Port)
                {
                    EnableSsl = cuenta.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(cuenta.From, cuenta.Password)
                };
                await client.SendMailAsync(message, ct);

                return new CatalogoPedidoEmailResultDto { Enviado = true };
            }
            catch (Exception ex)
            {
                ultimoError = ex;
                await appEvents.LogErrorAsync(
                    ModuleName, "EnviarConfirmacionPedido", ex, $"Fallo el envío con la cuenta {cuenta.From}.",
                    new { request.Pedido.IdComprobanteTexto, Destinatario = to, Cuenta = cuenta.From },
                    AppEventSeverity.Warning, ct);
            }
        }

        await appEvents.LogErrorAsync(
            ModuleName, "EnviarConfirmacionPedido", ultimoError ?? new InvalidOperationException("Sin cuentas de envío disponibles."),
            "Fallo el envío de email de confirmación de pedido con todas las cuentas configuradas.",
            new { request.Pedido.IdComprobanteTexto, Destinatario = to },
            AppEventSeverity.Error, ct);
        return new CatalogoPedidoEmailResultDto { Enviado = false, MensajeError = "No pudimos enviar el email de confirmación. Podés reintentar el envío más tarde." };
    }

    public async Task<bool> EnviarRecuperacionClaveAsync(
        string emailDestino,
        string nombreCliente,
        string nombreEmpresa,
        string? logoUrlAbsoluta,
        string urlRestablecer,
        CancellationToken ct = default)
    {
        var to = (emailDestino ?? string.Empty).Trim();
        if (to.Length == 0 || string.IsNullOrWhiteSpace(urlRestablecer))
            return false;

        try
        {
            _ = new MailAddress(to);
        }
        catch (FormatException)
        {
            return false;
        }

        IReadOnlyList<MailInfo> cuentas;
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            cuentas = await ResolveMailAccountsAsync(cn, ct);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                ModuleName, "EnviarRecuperacionClave", ex, "No se pudo resolver la configuración de correo saliente.",
                new { Destinatario = to }, AppEventSeverity.Warning, ct);
            return false;
        }

        var html = BuildRecuperacionClaveHtml(nombreCliente, nombreEmpresa, logoUrlAbsoluta, urlRestablecer);
        var asunto = string.IsNullOrWhiteSpace(nombreEmpresa) ? "Recuperar contraseña" : $"Recuperar contraseña - {nombreEmpresa.Trim()}";

        Exception? ultimoError = null;
        foreach (var cuenta in cuentas)
        {
            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(cuenta.From, string.IsNullOrWhiteSpace(nombreEmpresa) ? cuenta.From : nombreEmpresa.Trim()),
                    Subject = asunto,
                    Body = html,
                    IsBodyHtml = true
                };
                message.To.Add(to);

                using var client = new SmtpClient(cuenta.Server, cuenta.Port)
                {
                    EnableSsl = cuenta.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(cuenta.From, cuenta.Password)
                };
                await client.SendMailAsync(message, ct);
                return true;
            }
            catch (Exception ex)
            {
                ultimoError = ex;
                await appEvents.LogErrorAsync(
                    ModuleName, "EnviarRecuperacionClave", ex, $"Fallo el envío con la cuenta {cuenta.From}.",
                    new { Destinatario = to, Cuenta = cuenta.From }, AppEventSeverity.Warning, ct);
            }
        }

        await appEvents.LogErrorAsync(
            ModuleName, "EnviarRecuperacionClave", ultimoError ?? new InvalidOperationException("Sin cuentas de envío disponibles."),
            "Fallo el envío del email de recuperación de contraseña con todas las cuentas configuradas.",
            new { Destinatario = to }, AppEventSeverity.Error, ct);
        return false;
    }

    private static string BuildRecuperacionClaveHtml(string nombreCliente, string nombreEmpresa, string? logoUrlAbsoluta, string urlRestablecer)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Recuperar contraseña</title></head>");
        sb.Append("<body style=\"margin:0;padding:24px;background:#f1f5f9;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;\">");
        sb.Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #e2e8f0;\">");

        sb.Append("<div style=\"padding:22px 24px;background:linear-gradient(135deg,#0f172a,#1e3a8a);color:#ffffff;\">");
        if (!string.IsNullOrWhiteSpace(logoUrlAbsoluta))
            sb.Append("<img src=\"").Append(E(logoUrlAbsoluta)).Append("\" alt=\"").Append(E(nombreEmpresa))
              .Append("\" style=\"max-height:44px;max-width:220px;display:block;margin-bottom:10px;\" />");
        sb.Append("<div style=\"font-size:20px;font-weight:700;\">Recuperar contraseña</div>");
        sb.Append("</div>");

        sb.Append("<div style=\"padding:20px 24px;\">");
        sb.Append("<p style=\"font-size:14px;line-height:1.5;margin:0 0 12px;\">Hola").Append(string.IsNullOrWhiteSpace(nombreCliente) ? "" : $" {E(nombreCliente)}").Append(",</p>");
        sb.Append("<p style=\"font-size:14px;line-height:1.5;margin:0 0 20px;\">Recibimos una solicitud para restablecer tu contraseña de acceso al Portal Cliente. Si fuiste vos, hacé clic en el siguiente botón para definir una nueva contraseña:</p>");
        sb.Append("<div style=\"text-align:center;margin:0 0 20px;\">");
        sb.Append("<a href=\"").Append(E(urlRestablecer)).Append("\" style=\"display:inline-block;padding:12px 28px;background:#0ea5e9;color:#ffffff;text-decoration:none;border-radius:10px;font-weight:700;font-size:14px;\">Definir nueva contraseña</a>");
        sb.Append("</div>");
        sb.Append("<p style=\"font-size:12px;line-height:1.5;color:#64748b;margin:0 0 8px;\">Este enlace vence en 1 hora y solo puede usarse una vez.</p>");
        sb.Append("<p style=\"font-size:12px;line-height:1.5;color:#64748b;margin:0;\">Si no solicitaste este cambio, podés ignorar este email: tu contraseña actual sigue funcionando normalmente.</p>");
        sb.Append("</div></div></body></html>");
        return sb.ToString();
    }

    private static string BuildSubject(CatalogoPedidoEmailRequestDto request)
    {
        var p = request.Pedido;
        var empresa = string.IsNullOrWhiteSpace(request.NombreEmpresa) ? string.Empty : $" - {request.NombreEmpresa.Trim()}";
        return $"Pedido {p.Tc} {p.IdComprobanteTexto}{empresa}";
    }

    private static async Task<string> ReadConfigAsync(SqlConnection cn, string clave, CancellationToken ct)
        => (await cn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT TOP (1) ISNULL(VALOR, N'') FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);",
            new { Clave = clave }, cancellationToken: ct))) ?? string.Empty;

    private async Task<MailInfo> ResolveMailConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var server = Fallback(await ReadConfigAsync(cn, "EMAIL_SERVER", ct), "EMAIL_SERVER");
        var port = Fallback(await ReadConfigAsync(cn, "EMAIL_PORT", ct), "EMAIL_PORT");
        var account = Fallback(await ReadConfigAsync(cn, "EMAIL_CTA", ct), "EMAIL_CTA");
        var password = Fallback(await ReadConfigAsync(cn, "EMAIL_PASS", ct), "EMAIL_PASS");
        var ssl = Fallback(await ReadConfigAsync(cn, "EMAIL_SSL", ct), "EMAIL_SSL");

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(port)
            || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Falta configurar el correo saliente (EMAIL_SERVER, EMAIL_PORT, EMAIL_CTA, EMAIL_PASS).");
        if (!int.TryParse(port, out var smtpPort) || smtpPort <= 0)
            throw new InvalidOperationException("EMAIL_PORT no tiene un valor válido.");

        var enableSsl = ssl.Equals("SI", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("1", StringComparison.OrdinalIgnoreCase);
        return new MailInfo(server, smtpPort, account, password, enableSsl);
    }

    private async Task<IReadOnlyList<MailInfo>> ResolveMailAccountsAsync(SqlConnection cn, CancellationToken ct)
    {
        var primaria = await ResolveMailConfigAsync(cn, ct);
        var cuentas = new List<MailInfo> { primaria };

        var cuenta2 = Fallback(await ReadConfigAsync(cn, "EMAIL_CTA_2", ct), "EMAIL_CTA_2");
        var password2 = Fallback(await ReadConfigAsync(cn, "EMAIL_PASS_2", ct), "EMAIL_PASS_2");
        if (!string.IsNullOrWhiteSpace(cuenta2) && !string.IsNullOrWhiteSpace(password2))
            cuentas.Add(primaria with { From = cuenta2.Trim(), Password = password2.Trim() });

        return cuentas;
    }

    // Mismas claves que usa CentralRegistrationService para el email de verificación de cuenta
    // (RegistroPublico:EmailXxx), que sabemos que efectivamente llega. Se usan como prioridad antes
    // que la cuenta de Gmail por defecto, para que "pedidos" mande desde el mismo lugar que "registro".
    private static readonly Dictionary<string, string> RegistroPublicoKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EMAIL_SERVER"] = "RegistroPublico:EmailServer",
        ["EMAIL_PORT"] = "RegistroPublico:EmailPort",
        ["EMAIL_CTA"] = "RegistroPublico:EmailAccount",
        ["EMAIL_PASS"] = "RegistroPublico:EmailPassword",
        ["EMAIL_SSL"] = "RegistroPublico:EmailSsl"
    };

    private string Fallback(string dbValue, string key)
    {
        if (!string.IsNullOrWhiteSpace(dbValue))
            return dbValue.Trim();

        if (RegistroPublicoKeyMap.TryGetValue(key, out var registroPublicoKey)
            && configuration[registroPublicoKey] is { Length: > 0 } registroPublicoValue)
            return registroPublicoValue.Trim();

        return configuration[key] ?? configuration[$"PuntoVenta:{key}"] ?? string.Empty;
    }

    private sealed record MailInfo(string Server, int Port, string From, string Password, bool EnableSsl);

    private static string BuildPedidoHtml(CatalogoPedidoEmailRequestDto request)
    {
        var p = request.Pedido;
        var ar = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
        string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string Money(decimal v) => v.ToString("C2", ar);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Pedido ")
          .Append(E(p.IdComprobanteTexto)).Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:24px;background:#f1f5f9;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;\">");
        sb.Append("<div style=\"max-width:680px;margin:0 auto;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #e2e8f0;\">");

        sb.Append("<div style=\"padding:22px 24px;background:linear-gradient(135deg,#0f172a,#1e3a8a);color:#ffffff;\">");
        if (!string.IsNullOrWhiteSpace(request.LogoUrlAbsoluta))
            sb.Append("<img src=\"").Append(E(request.LogoUrlAbsoluta)).Append("\" alt=\"").Append(E(request.NombreEmpresa))
              .Append("\" style=\"max-height:44px;max-width:220px;display:block;margin-bottom:10px;\" />");
        sb.Append("<div style=\"font-size:20px;font-weight:700;\">Pedido recibido</div>");
        sb.Append("<div style=\"font-size:13px;opacity:.9;margin-top:4px;\">Pedido ").Append(E(p.Tc)).Append(' ').Append(E(p.IdComprobanteTexto))
          .Append(" &middot; ").Append(p.Fecha.ToString("dd/MM/yyyy", ar)).Append("</div>");
        sb.Append("</div>");

        sb.Append("<div style=\"padding:20px 24px;\">");
        sb.Append("<p style=\"font-size:14px;line-height:1.5;margin:0 0 16px;\">Recibimos tu pedido correctamente. A continuación encontrarás el detalle.</p>");
        sb.Append("<div style=\"font-size:14px;margin-bottom:16px;\"><strong>Cliente:</strong> ").Append(E(p.RazonSocial)).Append(" (").Append(E(p.CodigoCliente)).Append(")</div>");

        sb.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;font-size:13px;\">");
        sb.Append("<thead><tr style=\"background:#f1f5f9;\">");
        sb.Append("<th style=\"padding:8px;text-align:left;\"></th>");
        sb.Append("<th style=\"padding:8px;text-align:left;\">Artículo</th>");
        sb.Append("<th style=\"padding:8px;text-align:right;\">Cant.</th>");
        sb.Append("<th style=\"padding:8px;text-align:right;\">P. unit.</th>");
        sb.Append("<th style=\"padding:8px;text-align:right;\">Subtotal</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var l in p.Lineas)
        {
            var tieneImagen = request.ImagenesPorArticulo.TryGetValue(l.IdArticulo, out var img) && !string.IsNullOrWhiteSpace(img);
            sb.Append("<tr>");
            sb.Append("<td style=\"padding:8px;border-bottom:1px solid #f1f5f9;width:48px;\">");
            if (tieneImagen)
                sb.Append("<img src=\"").Append(E(img)).Append("\" width=\"40\" height=\"40\" style=\"border-radius:8px;object-fit:cover;display:block;\" />");
            sb.Append("</td>");
            sb.Append("<td style=\"padding:8px;border-bottom:1px solid #f1f5f9;\"><div style=\"font-weight:600;\">").Append(E(l.Descripcion))
              .Append("</div><div style=\"color:#64748b;font-size:11px;\">").Append(E(l.IdArticulo)).Append("</div></td>");
            sb.Append("<td style=\"padding:8px;border-bottom:1px solid #f1f5f9;text-align:right;\">").Append(l.Cantidad.ToString("0.##", ar)).Append("</td>");
            sb.Append("<td style=\"padding:8px;border-bottom:1px solid #f1f5f9;text-align:right;\">").Append(Money(l.PrecioUnitario)).Append("</td>");
            sb.Append("<td style=\"padding:8px;border-bottom:1px solid #f1f5f9;text-align:right;font-weight:600;\">").Append(Money(l.Subtotal)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<div style=\"text-align:right;font-size:16px;margin-top:16px;\">Total: <strong>").Append(Money(p.Total)).Append("</strong></div>");
        sb.Append("</div></div></body></html>");
        return sb.ToString();
    }
}
