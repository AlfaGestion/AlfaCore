using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Job en segundo plano que compacta los adjuntos repetidos de Conversaciones grabados antes del almacén por
/// contenido (ver ConversacionesService.AdjuntosCompactacion). Mientras alguna base tiene trabajo pendiente pasa
/// cada minuto (un lote por base); cuando todas terminaron, una vez por hora para lo que entre nuevo. Mismo
/// recorrido de bases que ConversacionesProgramadosHostedService. Se apaga por base con
/// CONVERSACIONES-ADJUNTOS-COMPACTAR = NO, o con ProcesosSegundoPlano:Deshabilitados.
/// </summary>
public sealed class ConversacionesAdjuntosCompactacionHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<ConversacionesAdjuntosCompactacionHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan IntervaloConTrabajo = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IntervaloSinTrabajo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Arranca después de las actualizaciones y de los demás jobs, para no sumar carga al inicio.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pendiente = false;
            try
            {
                pendiente = await EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en el job de compactación de adjuntos de conversaciones.");
            }

            try
            {
                await Task.Delay(pendiente ? IntervaloConTrabajo : IntervaloSinTrabajo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> EjecutarCicloAsync(CancellationToken ct)
    {
        if (!appMode.IsSaaSMode || string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
        {
            using var scope = services.CreateScope();
            var resultado = await scope.ServiceProvider.GetRequiredService<IConversacionesService>().CompactarAdjuntosAsync(ct);
            Informar(resultado, "local");
            return resultado.Habilitado && !resultado.Terminado;
        }

        using (var assignmentScope = services.CreateScope())
        {
            var workerAssignment = assignmentScope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>();
            if (!await workerAssignment.DebeEjecutarWorkersAsync(ct))
                return false;
        }

        IReadOnlyList<BaseCentralDto> bases;
        using (var scope = services.CreateScope())
            bases = await scope.ServiceProvider.GetRequiredService<IConversacionesHabilitacionService>().GetBasesHabilitadasAsync(ct);

        var pendiente = false;
        foreach (var b in bases)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = services.CreateScope();
                scope.ServiceProvider.GetRequiredService<ISessionService>().SetWebhookOverride(new SessionDto
                {
                    BaseId = b.IdBase,
                    Nombre = b.Nombre,
                    Servidor = b.DbServer,
                    BaseDatos = b.DbName,
                    Usuario = b.DbUser,
                    Password = b.DbPassword,
                    TrustServerCertificate = true
                });

                var resultado = await scope.ServiceProvider.GetRequiredService<IConversacionesService>().CompactarAdjuntosAsync(ct);
                Informar(resultado, b.Nombre);
                pendiente |= resultado.Habilitado && !resultado.Terminado;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "La compactación de adjuntos falló en la base {Base}.", b.Nombre);
            }
        }

        return pendiente;
    }

    private void Informar(ConversacionAdjuntosCompactacionResultDto r, string baseNombre)
    {
        if (!r.HuboCambios)
            return;

        logger.LogInformation(
            "Adjuntos compactados en {Base}: {Movidos} archivo(s) movidos ({MbDisco:N0} MB liberados en disco), {Compartidas} fila(s) compartidas ({MbBase:N0} MB en la base).",
            baseNombre, r.ArchivosMovidos, r.BytesLiberadosDisco / 1048576d, r.FilasCompartidas, r.BytesLiberadosBase / 1048576d);
    }
}
