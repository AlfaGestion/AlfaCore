using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;

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
            catch (SqlException ex) when (ex.Number == 4060)
            {
                logger.LogWarning(ex, "Worker de lectura automática de compras sin acceso a la base configurada. Se reintentará en {DelaySeconds}s.", delaySeconds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en worker de lectura automática de compras.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }
}
