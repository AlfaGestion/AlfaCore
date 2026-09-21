using AlfaCore.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Procesa la cola de lectura IA de compras solo de las bases autorizadas explícitamente en la central, en cada ciclo. No puede
/// depender de <see cref="IAppUserSessionService.CurrentUser"/> (el scope de un BackgroundService
/// nunca tiene sesión de usuario logueado, aunque haya gente usando la app en otro circuito Blazor)
/// asi que, para cada base, fuerza esa base como activa via
/// <see cref="ISessionService.SetWebhookOverride"/> ANTES de procesarla — el mismo mecanismo que ya
/// usan los webhooks de WhatsApp/Instagram/MercadoLibre para resolver el tenant sin sesión de
/// usuario. Un error en una base no interrumpe el procesamiento de las demás.
/// </summary>
public sealed class InterfacesCompraIaWorkerHostedService(
    IServiceProvider services,
    ILogger<InterfacesCompraIaWorkerHostedService> logger,
    InterfacesCompraIaWorkerState state) : BackgroundService
{
    private const int DefaultDelaySeconds = 15;

    private readonly Dictionary<int, bool> _reportedConnectionWarningByBase = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextDelaySeconds = DefaultDelaySeconds;

            try
            {
                nextDelaySeconds = await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error general en el worker de lectura automática de compras (listado de bases).");
            }

            await Task.Delay(TimeSpan.FromSeconds(nextDelaySeconds), stoppingToken);
        }
    }

    internal async Task<int> RunCycleAsync(CancellationToken stoppingToken)
    {
                using var scope = services.CreateScope();
                var workerAssignment = scope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>();
                if (!await workerAssignment.DebeEjecutarWorkersAsync(stoppingToken))
                    return DefaultDelaySeconds;

                var basesSvc = scope.ServiceProvider.GetRequiredService<ICentralCompraIaService>();

                var bases = await basesSvc.GetBasesHabilitadasAsync(stoppingToken);
                state.SetBasesHabilitadas(bases.Select(b => b.IdBase));
                var intervalosActivos = new List<int>();

                foreach (var b in bases)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var delaySecondsBase = DefaultDelaySeconds;

                    try
                    {
                        using var baseScope = services.CreateScope();
                        var sessionSvc = baseScope.ServiceProvider.GetRequiredService<ISessionService>();
                        sessionSvc.SetWebhookOverride(new SessionDto
                        {
                            BaseId = b.IdBase,
                            Nombre = b.Nombre,
                            Servidor = b.DbServer,
                            BaseDatos = b.DbName,
                            Usuario = b.DbUser,
                            Password = b.DbPassword,
                            TrustServerCertificate = true
                        });

                        var configSvc = baseScope.ServiceProvider.GetRequiredService<IInterfacesConfigService>();
                        var settings = await configSvc.GetCompraIaSettingsAsync(stoppingToken);
                        delaySecondsBase = Math.Max(3, settings.WorkerIntervaloSegundos);

                        if (settings.Habilitado && settings.WorkerHabilitado)
                        {
                            intervalosActivos.Add(delaySecondsBase);
                            state.MarkRunning(b.IdBase, "Ejecutando cola de compras.");
                            var interfacesSvc = baseScope.ServiceProvider.GetRequiredService<IInterfacesService>();
                            var processed = await interfacesSvc.ProcessCompraIaQueueAsync(stoppingToken);
                            state.MarkProcessed(b.IdBase, processed);
                            state.MarkWaiting(b.IdBase, delaySecondsBase, processed > 0
                                ? $"Worker ejecutó. Procesó {processed} registro(s) en el último ciclo."
                                : "Worker ejecutó. No encontró pendientes para procesar.");
                        }
                        else
                        {
                            state.MarkWaiting(b.IdBase, delaySecondsBase, "Worker deshabilitado por configuración.");
                        }

                        _reportedConnectionWarningByBase[b.IdBase] = false;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (SqlException ex) when (ex.Number == 4060 || ex.Number == 10060)
                    {
                        RegisterConnectionWarning(b.IdBase, delaySecondsBase, ex);
                    }
                    catch (AppUserFacingException ex) when (IsSqlLoginDatabaseError(ex))
                    {
                        RegisterConnectionWarning(b.IdBase, delaySecondsBase, ex);
                    }
                    catch (Exception ex)
                    {
                        _reportedConnectionWarningByBase[b.IdBase] = false;
                        state.MarkError(b.IdBase, delaySecondsBase, "Worker con error en el último ciclo.", ex.Message);
                        logger.LogError(ex, "Error en worker de lectura automática de compras para la base {IdBase} ({Nombre}).", b.IdBase, b.Nombre);
                    }
                }

                // La cadencia del ciclo compartido sigue al intervalo mas corto entre las bases
                // que realmente tienen el worker habilitado (si ninguna lo tiene, usa el default).
                return intervalosActivos.Count > 0 ? intervalosActivos.Min() : DefaultDelaySeconds;
    }

    private void RegisterConnectionWarning(int idBase, int delaySeconds, Exception ex)
    {
        state.MarkWarning(idBase, delaySeconds, "Worker sin acceso a la base configurada. Reintentará automáticamente.");

        if (_reportedConnectionWarningByBase.TryGetValue(idBase, out var already) && already)
            return;

        logger.LogWarning(ex, "Worker de lectura automática de compras sin acceso a la base {IdBase}. Se reintentará en {DelaySeconds}s.", idBase, delaySeconds);
        _reportedConnectionWarningByBase[idBase] = true;
    }

    private static bool IsSqlLoginDatabaseError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlEx && sqlEx.Number == 4060)
                return true;
        }

        return false;
    }
}
