namespace AlfaCore.Services;

public sealed class WhatsAppWebInboxHostedService(
    IServiceProvider services,
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
                using var scope = services.CreateScope();
                var conversaciones = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
                var processed = await conversaciones.ProcessWhatsAppWebInboxAsync(stoppingToken);
                if (processed > 0)
                    logger.LogInformation("WhatsApp Web inbox: {Count} mensaje(s) procesados.", processed);
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
}
