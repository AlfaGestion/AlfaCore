using System.Net;
using System.Net.Mail;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Job diario para la prueba gratuita autoservicio de módulos (ver
/// docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md): suspende las pruebas vencidas que nadie
/// convirtió en contrato pago, y manda un email de aviso a las que están por vencer. Solo actúa
/// en modo SaaS con <c>ConnectionStrings:AlfaCentral</c> configurado — en una instalación
/// on-premise de un solo cliente no existe el concepto de "módulo contratado".
/// </summary>
public sealed class ModuloPruebaRecordatorioHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<ModuloPruebaRecordatorioHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (appMode.IsSaaSMode && !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
            {
                try
                {
                    await EjecutarCicloAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error en el job de recordatorios de prueba de módulos.");
                }
            }

            try
            {
                await Task.Delay(Intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EjecutarCicloAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var adminService = scope.ServiceProvider.GetRequiredService<ICentralAdminService>();

        var expiradas = await adminService.ExpirarPruebasVencidasAsync(ct);
        if (expiradas > 0)
            logger.LogInformation("Se suspendieron {Cantidad} prueba(s) de módulo vencida(s).", expiradas);

        var pendientes = await adminService.GetPruebasPorVencerAsync(PruebaModuloDefaults.DiasAvisoPrevio, ct);
        foreach (var pendiente in pendientes)
        {
            try
            {
                await EnviarRecordatorioAsync(pendiente, ct);
                await adminService.MarcarRecordatorioPruebaEnviadoAsync(pendiente.IdCliente, pendiente.IdModulo, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo mandar el recordatorio de prueba a {Email} (módulo {Codigo}).", pendiente.Email, pendiente.Codigo);
            }
        }
    }

    private async Task EnviarRecordatorioAsync(PruebaModuloRecordatorioDto pendiente, CancellationToken ct)
    {
        var smtpServer = ResolveServerConfig("RegistroPublico:EmailServer", "EMAIL_SERVER");
        var smtpPort = ResolveServerConfig("RegistroPublico:EmailPort", "EMAIL_PORT");
        var smtpAccount = ResolveServerConfig("RegistroPublico:EmailAccount", "EMAIL_CTA");
        var smtpPassword = ResolveServerConfig("RegistroPublico:EmailPassword", "EMAIL_PASS");
        var smtpSsl = ResolveServerConfig("RegistroPublico:EmailSsl", "EMAIL_SSL");

        if (string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(smtpPort) ||
            string.IsNullOrWhiteSpace(smtpAccount) || string.IsNullOrWhiteSpace(smtpPassword))
        {
            throw new InvalidOperationException("Falta configurar el correo saliente del registro público en el servidor.");
        }

        if (!int.TryParse(smtpPort, out var port) || port <= 0)
            throw new InvalidOperationException("El puerto SMTP del registro público no es válido.");

        var diasRestantes = Math.Max(0, (int)Math.Ceiling((pendiente.PruebaVenceUtc - DateTime.UtcNow).TotalDays));

        using var message = new MailMessage
        {
            From = new MailAddress(smtpAccount.Trim()),
            Subject = $"Tu prueba de {pendiente.Nombre} vence en {diasRestantes} día(s)",
            Body = BuildRecordatorioHtml(pendiente, diasRestantes),
            IsBodyHtml = true
        };
        message.To.Add(pendiente.Email.Trim());

        using var client = new SmtpClient(smtpServer.Trim(), port)
        {
            EnableSsl = smtpSsl.Equals("SI", StringComparison.OrdinalIgnoreCase)
                        || smtpSsl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                        || smtpSsl.Equals("1", StringComparison.OrdinalIgnoreCase),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(smtpAccount.Trim(), smtpPassword.Trim())
        };

        await client.SendMailAsync(message, ct);
    }

    private string ResolveServerConfig(string primaryKey, string fallbackKey)
        => configuration[primaryKey]?.Trim() ?? configuration[fallbackKey]?.Trim() ?? string.Empty;

    private static string BuildRecordatorioHtml(PruebaModuloRecordatorioDto pendiente, int diasRestantes)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var precioTexto = pendiente.Precio > 0
            ? $"USD {pendiente.Precio:0.##}/mes"
            : "sin costo";

        return $"""
            <!DOCTYPE html>
            <html lang="es">
            <head><meta charset="utf-8" /><title>Aviso de vencimiento de prueba</title></head>
            <body style="font-family:Segoe UI,Arial,sans-serif;background:#f4f7fb;padding:24px;color:#142133;">
              <div style="max-width:720px;margin:0 auto;background:#ffffff;border:1px solid #d9e3ef;border-radius:18px;overflow:hidden;">
                <div style="padding:24px 28px;background:#0f2138;color:#ffffff;">
                  <h1 style="margin:0;font-size:24px;">Alfa Net Web</h1>
                  <p style="margin:8px 0 0;font-size:15px;opacity:.92;">Tu prueba gratuita está por vencer</p>
                </div>
                <div style="padding:28px;">
                  <p style="margin:0 0 16px;">Hola {E(pendiente.ClienteNombre)}.</p>
                  <p style="margin:0 0 16px;">
                    Tu prueba gratuita del módulo <strong>{E(pendiente.Nombre)}</strong> vence en
                    <strong>{diasRestantes} día(s)</strong> ({pendiente.PruebaVenceUtc:dd/MM/yyyy}).
                    Después de esa fecha, si no lo contratás, el módulo se desactiva automáticamente.
                  </p>
                  <p style="margin:0 0 16px;">Costo después de la prueba: <strong>{E(precioTexto)}</strong>.</p>
                  <p style="margin:0;">Si querés seguir usándolo, contactá a soporte para confirmar el pago y dejarlo activo sin interrupciones.</p>
                </div>
              </div>
            </body>
            </html>
            """;
    }
}
