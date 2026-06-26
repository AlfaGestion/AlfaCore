using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class InterfacesCompraIaWorkerHostedService(
    IServiceProvider services,
    ILogger<InterfacesCompraIaWorkerHostedService> logger,
    InterfacesCompraIaWorkerState state) : BackgroundService
{
    private bool _reportedConnectionWarning;

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
                RegisterConnectionWarning(delaySeconds, ex);
            }
            catch (SqlException ex) when (ex.Number == 10060)
            {
                RegisterConnectionWarning(delaySeconds, ex);
            }
            catch (AppUserFacingException ex) when (IsSqlLoginDatabaseError(ex))
            {
                RegisterConnectionWarning(delaySeconds, ex);
            }
            catch (InvalidOperationException ex) when (IsMissingSqlSessionConfiguration(ex))
            {
                state.MarkWarning(delaySeconds, "Worker sin sesión SQL activa. Seleccioná una base válida y reintentará automáticamente.");

                if (!_reportedConnectionWarning)
                {
                    logger.LogWarning(ex, "Worker de lectura automática de compras sin sesión SQL activa. Se reintentará en {DelaySeconds}s.", delaySeconds);
                    _reportedConnectionWarning = true;
                }
            }
            catch (Exception ex)
            {
                _reportedConnectionWarning = false;
                state.MarkError(delaySeconds, "Worker con error en el último ciclo.", ex.Message);
                logger.LogError(ex, "Error en worker de lectura automática de compras.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private void RegisterConnectionWarning(int delaySeconds, Exception ex)
    {
        state.MarkWarning(delaySeconds, "Worker sin acceso a la base configurada. Reintentará automáticamente.");

        if (_reportedConnectionWarning)
            return;

        logger.LogWarning(ex, "Worker de lectura automática de compras sin acceso a la base configurada. Se reintentará en {DelaySeconds}s.", delaySeconds);
        _reportedConnectionWarning = true;
    }

    private static bool IsSqlLoginDatabaseError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlEx && sqlEx.Number == 4060)
                return true;
        }

        return false;
    }

    private static bool IsMissingSqlSessionConfiguration(InvalidOperationException ex)
        => ex.Message.Contains("ConnectionStrings:AlfaGestion", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("sesión SQL activa", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("sesion SQL activa", StringComparison.OrdinalIgnoreCase);
}
