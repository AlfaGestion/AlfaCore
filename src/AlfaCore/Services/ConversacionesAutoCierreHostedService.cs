using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Job en segundo plano del auto-cierre por inactividad de Conversaciones. A diferencia de las
/// otras automatizaciones (disparadas por un mensaje entrante en el webhook), esta corre por
/// tiempo: cada tanto recorre las bases activas y, en cada una, procesa avisos y cierres.
/// En SaaS itera todas las bases (fijando la conexión de cada una como hace el webhook); en una
/// instalación local corre sobre la base configurada. El auto-cierre viene apagado por config, así
/// que si nadie lo activó, cada ciclo no hace nada.
/// </summary>
public sealed class ConversacionesAutoCierreHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<ConversacionesAutoCierreHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

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
                logger.LogError(ex, "Error en el job de auto-cierre de conversaciones.");
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

                    var conv = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
                    var n = await conv.ProcesarAutoCierreAsync(ct);
                    if (n > 0)
                        logger.LogInformation("Auto-cierre: {Acciones} acción(es) en base {Base}.", n, b.Nombre);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "El auto-cierre falló en la base {Base}.", b.Nombre);
                }
            }
        }
        else
        {
            using var scope = services.CreateScope();
            var conv = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
            await conv.ProcesarAutoCierreAsync(ct);
        }
    }
}
