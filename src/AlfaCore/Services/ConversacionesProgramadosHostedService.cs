using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Job en segundo plano de los mensajes programados de Conversaciones. Cada minuto recorre las bases
/// activas y envía los mensajes cuya hora ya llegó. En SaaS itera todas las bases (fijando la conexión
/// de cada una, como el webhook); en una instalación local corre sobre la base configurada.
/// </summary>
public sealed class ConversacionesProgramadosHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<ConversacionesProgramadosHostedService> logger) : BackgroundService
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
                logger.LogError(ex, "Error en el job de mensajes programados de conversaciones.");
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
                    var n = await conv.ProcesarMensajesProgramadosAsync(ct);
                    if (n > 0)
                        logger.LogInformation("Mensajes programados: {Enviados} enviado(s) en base {Base}.", n, b.Nombre);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "El envío de mensajes programados falló en la base {Base}.", b.Nombre);
                }
            }
        }
        else
        {
            using var scope = services.CreateScope();
            var conv = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
            await conv.ProcesarMensajesProgramadosAsync(ct);
        }
    }
}
