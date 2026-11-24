using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Avisos automáticos de Tickets (2026-11-24): cada 5 minutos recorre las bases y manda los avisos de plazos,
/// seguimientos, clientes prioritarios esperando, el resumen diario por técnico y la prioridad sugerida por IA de
/// los tickets del bot (<see cref="ITicketsService.ProcesarAvisosAsync"/>). Mismo recorrido de bases que
/// <see cref="ConversacionesProgramadosHostedService"/>. Cada aviso sale una sola vez (TICK_AVISOS).
/// </summary>
public sealed class TicketsAvisosHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<TicketsAvisosHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

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
                logger.LogError(ex, "Error en el job de avisos de tickets.");
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
                    await ProcesarAsync(scope.ServiceProvider, b.Nombre, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Los avisos de tickets fallaron en la base {Base}.", b.Nombre);
                }
            }
        }
        else
        {
            using var scope = services.CreateScope();
            await ProcesarAsync(scope.ServiceProvider, "local", ct);
        }
    }

    private async Task ProcesarAsync(IServiceProvider provider, string nombreBase, CancellationToken ct)
    {
        var tickets = provider.GetRequiredService<ITicketsService>();
        var resultado = await tickets.ProcesarAvisosAsync(manual: false, ct);
        if (resultado.Enviados > 0 || resultado.PrioridadesIa > 0)
            logger.LogInformation("Avisos de tickets en {Base}: {Mensaje}", nombreBase, resultado.Mensaje);
    }
}
