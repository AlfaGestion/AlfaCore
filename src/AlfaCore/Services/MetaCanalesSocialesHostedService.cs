using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Renueva solo el acceso de Instagram de las cuentas conectadas con la conexión guiada (dura 60 días;
/// se renueva cuando faltan <see cref="MetaCanalesSocialesOptions.InstagramRenovarDiasAntes"/> días).
/// Corre cada 6 horas. Facebook no lo necesita: el token de página no vence.
/// </summary>
public sealed class MetaCanalesSocialesHostedService(
    IServiceProvider services,
    IOptions<MetaCanalesSocialesOptions> options,
    IAppModeService appMode,
    ILogger<MetaCanalesSocialesHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.InstagramListo && appMode.IsSaaSMode)
                    await RenovarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en la renovación de accesos de Instagram.");
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

    private async Task RenovarAsync(CancellationToken ct)
    {
        IReadOnlyList<MetaCanalConexionDto> porVencer;
        using (var scope = services.CreateScope())
        {
            var workerAssignment = scope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>();
            if (!await workerAssignment.DebeEjecutarWorkersAsync(ct))
                return;

            var store = scope.ServiceProvider.GetRequiredService<MetaCanalesSocialesStore>();
            if (!store.CentralDisponible)
                return;
            var dias = Math.Clamp(options.Value.InstagramRenovarDiasAntes, 2, 55);
            porVencer = await store.GetInstagramPorVencerAsync(DateTime.UtcNow.AddDays(dias), ct);
        }

        foreach (var conexion in porVencer)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<MetaCanalesSocialesStore>();
            try
            {
                var baseInfo = await scope.ServiceProvider.GetRequiredService<ICentralBasesService>().GetByIdAsync(conexion.IdBase, ct);
                if (baseInfo is null)
                    continue;

                scope.ServiceProvider.GetRequiredService<ISessionService>().SetWebhookOverride(new SessionDto
                {
                    BaseId = baseInfo.IdBase,
                    Nombre = baseInfo.Nombre,
                    Servidor = baseInfo.DbServer,
                    BaseDatos = baseInfo.DbName,
                    Usuario = baseInfo.DbUser,
                    Password = baseInfo.DbPassword,
                    TrustServerCertificate = true
                });

                var configService = scope.ServiceProvider.GetRequiredService<IConversacionesConfigService>();
                var config = await configService.GetInstagramConfigAsync(ct);
                if (string.IsNullOrWhiteSpace(config.AccessToken)
                    || !string.Equals(config.InstagramAccountId?.Trim(), conexion.CuentaId, StringComparison.Ordinal))
                {
                    await store.RegistrarRenovacionAsync(conexion.IdConexion, null, "La base ya no tiene el acceso de esta cuenta. Volvé a conectar Instagram.", ct);
                    continue;
                }

                var client = scope.ServiceProvider.GetRequiredService<MetaCanalesSocialesClient>();
                var (token, venceUtc) = await client.InstagramRenovarAsync(config.AccessToken.Trim(), ct);
                config.AccessToken = token;
                await configService.SaveInstagramConfigAsSystemAsync(config, ct);
                await store.RegistrarRenovacionAsync(conexion.IdConexion, venceUtc ?? DateTime.UtcNow.AddDays(60), null, ct);
                logger.LogInformation("Acceso de Instagram renovado para la base {Base} (cuenta {Cuenta}).", baseInfo.Nombre, conexion.CuentaId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo renovar el acceso de Instagram de la base {IdBase}.", conexion.IdBase);
                try
                {
                    await store.RegistrarRenovacionAsync(conexion.IdConexion, null,
                        ex is MetaCanalSocialException ? ex.Message : "No se pudo renovar el acceso de Instagram.", ct);
                }
                catch (Exception inner)
                {
                    logger.LogWarning(inner, "No se pudo registrar el error de renovación de Instagram.");
                }
            }
        }
    }
}
