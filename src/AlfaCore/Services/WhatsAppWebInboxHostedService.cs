using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class WhatsAppWebInboxHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<WhatsAppWebInboxHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
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
                logger.LogError(ex, "Error procesando el inbox de WhatsApp Web.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EjecutarCicloAsync(CancellationToken ct)
    {
        if (appMode.IsSaaSMode && !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
        {
            IReadOnlyList<BaseCentralDto> bases;
            using (var scope = services.CreateScope())
            {
                var basesSvc = scope.ServiceProvider.GetRequiredService<ICentralBasesService>();
                bases = await basesSvc.GetAllAsync(ct);
            }

            foreach (var b in bases)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var scope = services.CreateScope();
                    var session = scope.ServiceProvider.GetRequiredService<ISessionService>();
                    session.SetWebhookOverride(new SessionDto
                    {
                        BaseId = b.IdBase,
                        Nombre = b.Nombre,
                        Servidor = b.DbServer,
                        BaseDatos = b.DbName,
                        Usuario = b.DbUser,
                        Password = b.DbPassword,
                        TrustServerCertificate = true
                    });

                    var conversaciones = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
                    var processed = await conversaciones.ProcessWhatsAppWebInboxAsync(ct);
                    if (processed > 0)
                        logger.LogInformation("WhatsApp Web inbox: {Count} mensaje(s) procesados en base {Base}.", processed, b.Nombre);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error procesando el inbox de WhatsApp Web en base {Base}.", b.Nombre);
                }
            }

            return;
        }

        using (var scope = services.CreateScope())
        {
            var conversaciones = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
            var processed = await conversaciones.ProcessWhatsAppWebInboxAsync(ct);
            if (processed > 0)
                logger.LogInformation("WhatsApp Web inbox: {Count} mensaje(s) procesados.", processed);
        }
    }
}
