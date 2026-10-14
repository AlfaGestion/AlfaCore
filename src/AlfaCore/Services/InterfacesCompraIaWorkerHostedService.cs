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

    /// <summary>Tiempo máximo para comprobar que la base responde antes de procesarla.</summary>
    internal const int SegundosPruebaConexion = 5;

    /// <summary>Tope de espera entre reintentos de una base caída.</summary>
    internal const int MinutosMaximoReintento = 30;

    private readonly Dictionary<int, bool> _reportedConnectionWarningByBase = new();

    // Bases que no responden (servidores de clientes por VPN que se apagan o se desconectan): se
    // reintentan cada vez más espaciado en vez de en cada ciclo. Antes cada base caída costaba entre
    // 8 y 22 s de espera por ciclo (demorando a las que sí andan) y anotaba el error en AUX_ERR
    // cada 15 s (~7.000 filas por día en producción, 2026-10).
    private readonly Dictionary<int, BaseCaida> _basesCaidas = new();

    private sealed record BaseCaida(int Fallas, DateTime ReintentarDesde, DateTime Desde);

    /// <summary>Espera antes del próximo intento: 1, 2, 4, 8, 16 y luego 30 minutos.</summary>
    internal static TimeSpan EsperaReintento(int fallas)
        => TimeSpan.FromMinutes(Math.Min(MinutosMaximoReintento, Math.Pow(2, Math.Max(0, fallas - 1))));

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

                    if (_basesCaidas.TryGetValue(b.IdBase, out var caida) && DateTime.Now < caida.ReintentarDesde)
                    {
                        state.MarkWarning(b.IdBase, delaySecondsBase,
                            $"Sin acceso a la base desde {caida.Desde:dd/MM HH:mm}. Reintenta a las {caida.ReintentarDesde:HH:mm}.");
                        continue;
                    }

                    if (!await BaseRespondeAsync(b, stoppingToken))
                    {
                        await RegistrarCaidaAsync(b, scope.ServiceProvider, stoppingToken);
                        continue;
                    }

                    if (_basesCaidas.Remove(b.IdBase, out var recuperada))
                        await RegistrarRecuperacionAsync(b, recuperada, scope.ServiceProvider, stoppingToken);

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

    /// <summary>Prueba rápida de conexión (solo abre y cierra) con poco tiempo de espera.</summary>
    private static async Task<bool> BaseRespondeAsync(BaseCentralDto b, CancellationToken ct)
    {
        try
        {
            var csb = new SqlConnectionStringBuilder
            {
                DataSource = b.DbServer,
                InitialCatalog = b.DbName,
                UserID = b.DbUser,
                Password = b.DbPassword,
                TrustServerCertificate = true,
                ConnectTimeout = SegundosPruebaConexion,
                Pooling = false
            };
            await using var cn = new SqlConnection(csb.ConnectionString);
            await cn.OpenAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Base que no responde: se anota una sola vez por caída (no en cada ciclo) y se espera cada vez
    /// más antes de reintentar.
    /// </summary>
    private async Task RegistrarCaidaAsync(BaseCentralDto b, IServiceProvider sp, CancellationToken ct)
    {
        var ahora = DateTime.Now;
        var anterior = _basesCaidas.GetValueOrDefault(b.IdBase);
        var fallas = (anterior?.Fallas ?? 0) + 1;
        var caida = new BaseCaida(fallas, ahora + EsperaReintento(fallas), anterior?.Desde ?? ahora);
        _basesCaidas[b.IdBase] = caida;
        state.MarkWarning(b.IdBase, DefaultDelaySeconds,
            $"Sin acceso a la base desde {caida.Desde:dd/MM HH:mm}. Reintenta a las {caida.ReintentarDesde:HH:mm}.");

        if (anterior is not null)
            return;

        logger.LogWarning("Lectura de compras con IA: la base {IdBase} ({Nombre}, servidor {Servidor}) no responde. Se reintenta cada vez más espaciado.",
            b.IdBase, b.Nombre, b.DbServer);
        try
        {
            await sp.GetRequiredService<IAppEventService>().LogErrorAsync("Interfaces", "CompraIaBaseSinAcceso",
                new InvalidOperationException($"La base {b.Nombre} (servidor {b.DbServer}) no responde."),
                $"La lectura de compras con IA no puede conectarse a la base {b.Nombre} (servidor {b.DbServer}). Se reintenta sola, cada vez más espaciado (hasta cada {MinutosMaximoReintento} minutos). Se anota una sola vez por caída.",
                new { b.IdBase, b.Nombre, b.DbServer }, AppEventSeverity.Warning, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo registrar la caída de la base {IdBase} en AUX_ERR.", b.IdBase);
        }
    }

    private async Task RegistrarRecuperacionAsync(BaseCentralDto b, BaseCaida caida, IServiceProvider sp, CancellationToken ct)
    {
        logger.LogInformation("Lectura de compras con IA: la base {IdBase} ({Nombre}) volvió a responder.", b.IdBase, b.Nombre);
        try
        {
            await sp.GetRequiredService<IAppEventService>().LogAuditAsync("Interfaces", "CompraIaBaseRecuperada", "bases",
                b.IdBase.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"La lectura de compras con IA volvió a conectarse a la base {b.Nombre} (sin acceso desde {caida.Desde:dd/MM HH:mm}).",
                new { b.IdBase, b.Nombre, b.DbServer, caida.Fallas }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo registrar la recuperación de la base {IdBase}.", b.IdBase);
        }
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
