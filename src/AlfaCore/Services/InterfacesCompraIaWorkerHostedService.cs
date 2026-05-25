using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class InterfacesCompraIaWorkerHostedService(
    IServiceProvider services,
    ILogger<InterfacesCompraIaWorkerHostedService> logger,
    InterfacesCompraIaWorkerState state) : BackgroundService
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
                {
                    state.MarkRunning("Ejecutando cola de compras.");
                    var processed = await interfacesSvc.ProcessCompraIaQueueAsync(stoppingToken);
                    state.MarkProcessed(processed);
                    state.MarkWaiting(delaySeconds, processed > 0
                        ? $"Worker ejecutado. Procesó {processed} registro(s) en el último ciclo."
                        : "Worker ejecutado. No encontró pendientes para procesar.");
                }
                else
                {
                    state.MarkWaiting(delaySeconds, "Worker deshabilitado por configuración.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SqlException ex) when (ex.Number == 4060)
            {
                state.MarkWarning(delaySeconds, "Worker sin acceso a la base configurada. Reintentará automáticamente.");
                logger.LogWarning("Worker de lectura automática de compras sin acceso a la base configurada. Error SQL 4060. Se reintentará en {DelaySeconds}s.", delaySeconds);
            }
            catch (Exception ex)
            {
                state.MarkError(delaySeconds, "Worker con error en el último ciclo.", ex.Message);
                logger.LogError(ex, "Error en worker de lectura automática de compras.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }
}
