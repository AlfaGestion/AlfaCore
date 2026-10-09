using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Procesa recordatorios WhatsApp de Calendario (guardias, reuniones y capacitaciones) cuya fecha
/// programada ya venció. Antes solo existía el botón manual "Enviar ahora", por eso podían quedar
/// indefinidamente en PENDIENTE.
/// </summary>
public sealed class CalendarioRecordatoriosHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<CalendarioRecordatoriosHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);

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
                logger.LogError(ex, "Error en el job de recordatorios de Calendario.");
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
            using (var assignmentScope = services.CreateScope())
            {
                var workerAssignment = assignmentScope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>();
                if (!await workerAssignment.DebeEjecutarWorkersAsync(ct))
                    return;
            }

            IReadOnlyList<BaseCentralDto> bases;
            using (var scope = services.CreateScope())
            {
                var habilitacionSvc = scope.ServiceProvider.GetRequiredService<IConversacionesHabilitacionService>();
                bases = await habilitacionSvc.GetBasesHabilitadasAsync(ct);
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

                    var calendario = scope.ServiceProvider.GetRequiredService<ICalendarioService>();
                    var enviados = await calendario.ProcesarRecordatoriosPendientesAsync(ct);
                    if (enviados > 0)
                        logger.LogInformation("Calendario: {Cantidad} recordatorio(s) enviado(s) en base {Base}.", enviados, b.Nombre);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "El envío de recordatorios de Calendario falló en la base {Base}.", b.Nombre);
                }
            }
        }
        else
        {
            using var scope = services.CreateScope();
            var calendario = scope.ServiceProvider.GetRequiredService<ICalendarioService>();
            var enviados = await calendario.ProcesarRecordatoriosPendientesAsync(ct);
            if (enviados > 0)
                logger.LogInformation("Calendario: {Cantidad} recordatorio(s) enviado(s).", enviados);
        }
    }
}
