using Microsoft.Extensions.Hosting;

namespace AlfaCore.Services;

public sealed class InterfacesCompraIaWorkerHostedService(
    IServiceProvider services,
    ILogger<InterfacesCompraIaWorkerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delaySeconds = 10;

            try
            {
                using var scope = services.CreateScope();
                var configSvc = scope.ServiceProvider.GetRequiredService<IInterfacesConfigService>();
                var interfacesSvc = scope.ServiceProvider.GetRequiredService<IInterfacesService>();
                var settings = await configSvc.GetCompraIaSettingsAsync(stoppingToken);
                delaySeconds = Math.Max(3, settings.WorkerIntervaloSegundos);

                if (settings.Habilitado && settings.WorkerHabilitado)
                    await interfacesSvc.ProcessCompraIaQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en worker de lectura automática de compras.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }
}
