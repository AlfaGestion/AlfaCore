using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Job en segundo plano de la espera del Asistente IA de Conversaciones ("esperar N minutos antes de
/// responder", para darle margen a un agente humano). Cada minuto recorre las bases activas y retoma
/// las conversaciones encoladas cuya espera ya se cumplió. En SaaS itera todas las bases (fijando la
/// conexión de cada una, como el webhook); en una instalación local corre sobre la base configurada.
/// </summary>
public sealed class ConversacionesBotEsperaHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<ConversacionesBotEsperaHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

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
                logger.LogError(ex, "Error en el job de espera del Asistente IA de conversaciones.");
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
                    var n = await conv.ProcesarRespuestasBotPendientesAsync(ct);
                    if (n > 0)
                        logger.LogInformation("Espera del Asistente IA: {Procesadas} conversación(es) en base {Base}.", n, b.Nombre);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "La espera del Asistente IA falló en la base {Base}.", b.Nombre);
                }
            }
        }
        else
        {
            using var scope = services.CreateScope();
            var conv = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
            await conv.ProcesarRespuestasBotPendientesAsync(ct);
        }
    }
}
