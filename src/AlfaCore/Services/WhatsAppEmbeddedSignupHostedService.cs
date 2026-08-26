using AlfaCore.Configuration;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class WhatsAppEmbeddedSignupHostedService(
    IServiceProvider services,
    IOptions<WhatsAppEmbeddedSignupOptions> options,
    ILogger<WhatsAppEmbeddedSignupHostedService> logger) : BackgroundService
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_options.Enabled || !_options.WorkerEnabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.WorkerIntervalSeconds)), stoppingToken);
                continue;
            }

            try
            {
                using var scope = services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IWhatsAppEmbeddedSignupStore>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IWhatsAppEmbeddedSignupOrchestrator>();
                var now = DateTime.UtcNow;
                var item = await store.ClaimNextAsync(_workerId, now, now.AddMinutes(2), stoppingToken);
                if (item is not null)
                {
                    // ES-1 no habilita el feature. Este camino queda preparado para que ES-2 ejecute un solo paso.
                    await orchestrator.ProcessNextStepAsync(item.IdOnboarding, stoppingToken);
                    await store.ReleaseClaimAsync(item.IdOnboarding, _workerId, null, stoppingToken);
                }
            }
            catch (NotSupportedException ex)
            {
                logger.LogWarning("Worker Embedded Signup detenido en fundación ES-1: {Reason}", ex.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en worker de WhatsApp Embedded Signup {WorkerId}.", _workerId);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.WorkerIntervalSeconds)), stoppingToken);
        }
    }
}
