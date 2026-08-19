namespace AlfaCore.Services;

/// <summary>
/// Job diario del motor de facturación de suscripciones (ver
/// docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, Fases 3/6): genera los cargos de los módulos
/// que llegaron a su <c>FechaProximoCobro</c> y después procesa gracia/suspensión de los que
/// quedaron en mora. Mismo gate que <see cref="ModuloPruebaRecordatorioHostedService"/> (solo corre
/// en modo SaaS con <c>ConnectionStrings:AlfaCentral</c> configurado), pero con intervalo diario —
/// a diferencia de los avisos de prueba gratuita, la facturación no necesita chequearse cada 6hs.
/// </summary>
public sealed class BillingHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<BillingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (appMode.IsSaaSMode && !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
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
                    logger.LogError(ex, "Error en el job de facturación de suscripciones.");
                }
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
        using var scope = services.CreateScope();
        var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

        await billingService.ProcesarVencimientosAsync(ct);
        await billingService.ProcesarGraciaYSuspensionAsync(ct);
    }
}
