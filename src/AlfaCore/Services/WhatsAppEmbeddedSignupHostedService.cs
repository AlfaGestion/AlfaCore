using AlfaCore.Configuration;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class WhatsAppEmbeddedSignupHostedService(
    IServiceProvider services,
    IOptions<WhatsAppEmbeddedSignupOptions> options,
    ILogger<WhatsAppEmbeddedSignupHostedService> logger) : BackgroundService
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_options.Enabled || !_options.WorkerEnabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.WorkerIntervalSeconds)), stoppingToken);
                continue;
            }

            try
            {
                using var scope = services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IWhatsAppEmbeddedSignupStore>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IWhatsAppEmbeddedSignupOrchestrator>();
                var now = DateTime.UtcNow;
                // AllowAllTenants => procesa onboardings de cualquier base (derivado de datos
                // centrales). Lista no vacía => sólo esas. Lista vacía sin AllowAllTenants => no se
                // arrancan onboardings nuevos, pero los assets/onboardings ES existentes no se degradan.
                var item = _options.AllowAllTenants
                    ? await store.ClaimNextAsync(_workerId, now, now.AddMinutes(2), stoppingToken)
                    : _options.AllowedBaseIds.Length > 0
                        ? await store.ClaimNextForBasesAsync(_workerId, _options.AllowedBaseIds, now, now.AddMinutes(2), stoppingToken)
                        : null;
                if (item is not null)
                {
                    try
                    {
                        if (item.Status == AlfaCore.Models.WhatsAppEmbeddedOnboardingStatus.Importing)
                        {
                            var importer = scope.ServiceProvider.GetRequiredService<IWhatsAppEmbeddedOperationalImportService>();
                            await importer.CompleteForBaseAsync(item.IdOnboarding, item.IdBase, stoppingToken);
                        }
                        else
                        {
                            await orchestrator.ProcessNextStepAsync(item.IdOnboarding, stoppingToken);
                        }
                        await store.ReleaseClaimAsync(item.IdOnboarding, _workerId, null, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await HandleItemFailureAsync(scope.ServiceProvider, store, item, ex, stoppingToken);
                    }
                }
            }
            catch (NotSupportedException ex)
            {
                logger.LogWarning("Worker Embedded Signup detenido en fundación ES-1: {Reason}", ex.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en worker de WhatsApp Embedded Signup {WorkerId}.", _workerId);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.WorkerIntervalSeconds)), stoppingToken);
        }
    }

    private async Task HandleItemFailureAsync(
        IServiceProvider serviceProvider,
        IWhatsAppEmbeddedSignupStore store,
        AlfaCore.Models.WhatsAppEmbeddedOnboardingDto item,
        Exception exception,
        CancellationToken ct)
    {
        logger.LogError(exception, "Error procesando onboarding Embedded Signup {IdOnboarding} en {Step}.", item.IdOnboarding, item.CurrentStep);

        var metaException = exception as MetaWhatsAppManagementException;
        var errorCode = metaException?.ErrorCode ?? (item.Status == AlfaCore.Models.WhatsAppEmbeddedOnboardingStatus.Importing
            ? "OPERATIONAL_IMPORT_FAILED"
            : "WORKER_STEP_FAILED");
        var now = DateTime.UtcNow;
        var nextAttempt = WhatsAppEmbeddedSignupStateMachine.ScheduleRetry(
            now,
            item.RetryCount,
            _options.RetryInitialDelaySeconds,
            _options.RetryMaxDelaySeconds);
        if (metaException?.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
            nextAttempt = now.Add(retryAfter);
        if (metaException?.IsRateLimit == true)
            nextAttempt = WhatsAppEmbeddedSignupStateMachine.ScheduleRateLimitRetry(
                now, item.RetryCount, metaException.RetryAfter, metaException.EstimatedTimeToRegainAccess);
        var incident = string.Empty;

        try
        {
            var errorLogger = serviceProvider.GetRequiredService<IWhatsAppEmbeddedSignupErrorLogger>();
            incident = await errorLogger.LogAsync(item.IdOnboarding, item.IdBase, item.CurrentStep, errorCode, null, null, item.RetryCount, ct);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "No se pudo registrar el incidente ES de {IdOnboarding}.", item.IdOnboarding);
        }

        try
        {
            if (metaException?.RequiresReauthorization == true)
            {
                await store.MarkActionRequiredAsync(item.IdOnboarding,
                    AlfaCore.Models.WhatsAppEmbeddedActionRequiredReason.ReauthorizationRequired,
                    "Meta requiere renovar la autorización antes de continuar.", incident, ct);
                await store.ReleaseClaimAsync(item.IdOnboarding, _workerId, null, ct);
                return;
            }

            if (metaException?.IsRateLimit == true)
            {
                await store.ScheduleRetryAsync(item.IdOnboarding,
                    AlfaCore.Models.WhatsAppEmbeddedOnboardingStatus.Importing,
                    "READY_FOR_OPERATIONAL_UPSERT",
                    metaException.ErrorCode,
                    "Meta está demorando temporalmente la activación. AlfaCore continuará automáticamente.",
                    incident,
                    nextAttempt,
                    ct);
                await store.ReleaseClaimAsync(item.IdOnboarding, _workerId, nextAttempt, ct);
                return;
            }

            if (metaException is { IsTransient: false })
            {
                var retryAfterDescription = metaException.RetryAfter is { } retryAfterValue
                    ? $"{retryAfterValue.TotalSeconds:0} segundos"
                    : "sin dato";
                var detail = $"Meta rechazó la lectura necesaria (HTTP {metaException.HttpStatusCode?.ToString() ?? "desconocido"}, código {metaException.ErrorCode}, subcódigo {metaException.ErrorSubcode ?? "sin dato"}, transitorio=no, Retry-After={retryAfterDescription}).";
                await store.MarkActionRequiredAsync(item.IdOnboarding,
                    AlfaCore.Models.WhatsAppEmbeddedActionRequiredReason.CustomerActionRequired,
                    detail, incident, ct);
                await store.ReleaseClaimAsync(item.IdOnboarding, _workerId, null, ct);
                return;
            }

            if (item.RetryCount < _options.MaxRetryCount)
            {
                if (item.Status == AlfaCore.Models.WhatsAppEmbeddedOnboardingStatus.Importing)
                {
                    await store.ScheduleRetryAsync(item.IdOnboarding,
                        AlfaCore.Models.WhatsAppEmbeddedOnboardingStatus.Importing,
                        "READY_FOR_OPERATIONAL_UPSERT",
                        errorCode,
                        "No se pudo completar temporalmente el alta operativa.",
                        incident,
                        nextAttempt,
                        ct);
                }
                else
                {
                    await store.MarkRetryableFailureAsync(item.IdOnboarding, errorCode,
                        "No se pudo completar temporalmente la configuracion automatica.", incident, nextAttempt, ct);
                }
                await store.ReleaseClaimAsync(item.IdOnboarding, _workerId, nextAttempt, ct);
            }
            else
            {
                await store.MarkFinalFailureAsync(item.IdOnboarding, errorCode,
                    item.Status == AlfaCore.Models.WhatsAppEmbeddedOnboardingStatus.Importing
                        ? "No se pudo completar el alta operativa."
                        : "No se pudo completar la configuracion automatica.",
                    incident, ct);
                await store.ReleaseClaimAsync(item.IdOnboarding, _workerId, null, ct);
            }
        }
        catch (Exception stateException)
        {
            logger.LogError(stateException, "No se pudo liberar el onboarding Embedded Signup {IdOnboarding} tras un error.", item.IdOnboarding);
        }
    }
}
