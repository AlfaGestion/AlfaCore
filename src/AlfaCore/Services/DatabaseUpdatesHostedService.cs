using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class DatabaseUpdatesHostedService(
    IServiceProvider serviceProvider,
    ILogger<DatabaseUpdatesHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            using var scope = serviceProvider.CreateScope();
            var updates = scope.ServiceProvider.GetRequiredService<IActualizacionesService>();

            var result = await updates.ExecutePendingAsync(new ActualizacionesRunRequest
            {
                UsuarioAccion = "SYSTEM",
                PcAccion = Environment.MachineName,
                EsEjecucionAutomatica = true
            }, stoppingToken);

            if (result.SinCambios)
            {
                logger.LogInformation("No hay actualizaciones pendientes de base para aplicar.");
                return;
            }

            logger.LogInformation(
                "Se aplicaron {Count} actualizaciones de base. Versión final: {Version}. Ruta: {Path}",
                result.CantidadAplicada,
                result.VersionFinal,
                result.RutaOrigen);
        }
        catch (OperationCanceledException)
        {
            // Cierre normal del host.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudieron ejecutar las actualizaciones automáticas de base al iniciar.");
        }
    }
}
