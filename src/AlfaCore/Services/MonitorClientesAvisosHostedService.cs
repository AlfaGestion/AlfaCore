using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Avisos del Monitor de Clientes al chat de soporte (fase 2, 2026-10-28). Cada 5 minutos (o al minuto si
/// alguien tocó "Enviar aviso ahora") evalúa el tablero, libera el seguimiento de lo que se normalizó y,
/// según <see cref="MonitorClientesAvisos"/>, publica un único mensaje agrupado en el hilo interno
/// "Monitor de Clientes" de Conversaciones de ALFANET2007 y manda un push a los usuarios configurados.
/// Corre sin sesión: Conversaciones y push usan la conexión <c>AlfaGestion</c> (ALFANET2007), igual que el
/// monitor. En SaaS solo corre en el servidor que tiene asignados los workers.
/// </summary>
public sealed class MonitorClientesAvisosHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IAppModeService appMode,
    ILogger<MonitorClientesAvisosHostedService> logger) : BackgroundService
{
    internal const string NombreHilo = "Monitor de Clientes";
    private const string SistemaAccion = "MONITOR";
    private const string UrlTablero = "/admin/monitor-clientes";

    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Ciclo = TimeSpan.FromMinutes(5);

    private DateTime _ultimoCiclo = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Margen para que el reinicio no avise antes de que lleguen los primeros reportes.
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
                await EjecutarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en los avisos del Monitor de Clientes.");
            }

            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EjecutarAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaGestion")))
            return;

        using var scope = services.CreateScope();
        if (appMode.IsSaaSMode && !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
        {
            var workerAssignment = scope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>();
            if (!await workerAssignment.DebeEjecutarWorkersAsync(ct))
                return;
        }

        var monitor = scope.ServiceProvider.GetRequiredService<IMonitorClientesService>();
        var config = await monitor.GetAvisoConfigAsync(ct);
        var ahora = DateTime.Now;
        if (!config.Forzar && ahora - _ultimoCiclo < Ciclo)
            return;
        _ultimoCiclo = ahora;

        var tablero = await monitor.GetTableroAsync(ct);
        await monitor.LimpiarSeguimientosAsync(tablero, ct);
        // Después de limpiar, una fila que se normalizó ya no figura como tomada.
        tablero = await monitor.GetTableroAsync(ct);

        var forzar = config.Forzar;
        if (!config.Activo && !forzar)
            return;

        await AvisarBackupsAsync(scope.ServiceProvider, config, tablero, ct);

        var pendientes = MonitorClientesAvisos.Pendientes(tablero.Filas, config);
        var decision = MonitorClientesAvisos.Decidir(pendientes, config, ahora, forzar);
        var anteriores = MonitorClientesAvisos.ClavesDeFirma(config.UltimaFirma);

        if (decision != MonitorClientesAvisos.Decision.Nada)
        {
            var texto = decision == MonitorClientesAvisos.Decision.Alertas
                ? MonitorClientesAvisos.MensajeAlertas(pendientes, anteriores, config)
                : MonitorClientesAvisos.MensajeNormalizado(forzar && anteriores.Count == 0);

            config.IdHilo = await PublicarAsync(scope.ServiceProvider, config.IdHilo, NombreHilo, AvisoMonitor, texto, ct);
            config.UltimoEnvio = ahora;

            if (decision == MonitorClientesAvisos.Decision.Alertas && config.UsuariosPushLista.Count > 0)
            {
                try
                {
                    var push = scope.ServiceProvider.GetRequiredService<INotificacionesPushService>();
                    await push.NotifyAvisoAsync(config.UsuariosPushLista, NombreHilo, MonitorClientesAvisos.TextoPush(pendientes), UrlTablero, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Monitor de Clientes: no se pudo mandar el push del aviso.");
                }
            }

            logger.LogInformation("Monitor de Clientes: aviso {Decision} con {Pendientes} alerta(s) en el hilo {IdHilo}.",
                decision, pendientes.Count, config.IdHilo);
        }

        // La firma sigue a las alertas pendientes aunque no se mande nada: si una se toma y después
        // vuelve a quedar libre, cuenta como nueva; si se vacía, el próximo ciclo avisa "normalizado".
        config.UltimaFirma = MonitorClientesAvisos.Firma(pendientes);
        config.Forzar = false;
        await monitor.GuardarAvisoEstadoAsync(config, ct);
    }

    /// <summary>
    /// Backup diario fallido o recuperado → hilo "Backups de clientes" (2026-11-27). Un mensaje solo cuando hay
    /// algo nuevo; la firma guarda qué backups fallaban para no repetir. Un error acá no frena el aviso general.
    /// </summary>
    private async Task AvisarBackupsAsync(IServiceProvider sp, MonitorAvisoConfigDto config, MonitorTableroDto tablero, CancellationToken ct)
    {
        try
        {
            var fallidos = MonitorClientesAvisos.BackupsFallidos(tablero.Filas);
            var anteriores = MonitorClientesAvisos.ClavesDeFirma(config.BackupFirma);
            var texto = MonitorClientesAvisos.MensajeBackups(fallidos, anteriores, tablero.Filas);
            if (texto is not null)
            {
                config.BackupIdHilo = await PublicarAsync(sp, config.BackupIdHilo, NombreHiloBackups, AvisoBackups, texto, ct);
                logger.LogInformation("Monitor de Clientes: aviso de backups ({Fallidos} con problemas) en el hilo {IdHilo}.", fallidos.Count, config.BackupIdHilo);
            }
            config.BackupFirma = MonitorClientesAvisos.Firma(fallidos);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Monitor de Clientes: no se pudo avisar los backups fallidos.");
        }
    }

    internal const string NombreHiloBackups = "Backups de clientes";
    private const string AvisoMonitor = "MONITOR";
    private const string AvisoBackups = "MONITOR-BACKUP";

    /// <summary>
    /// Publica en el hilo interno de avisos; si no existe (o lo borraron), crea uno nuevo. El hilo queda marcado
    /// como aviso del sistema: se ve en la bandeja Todos, resaltado (el chat interno no lo miraba nadie).
    /// </summary>
    private static async Task<long> PublicarAsync(IServiceProvider sp, long? idHilo, string nombreHilo, string aviso, string texto, CancellationToken ct)
    {
        var conversaciones = sp.GetRequiredService<IConversacionesService>();

        if (idHilo is > 0)
        {
            var hilo = await conversaciones.GetConversationAsync(idHilo.Value, ct);
            if (hilo is null || !string.Equals(hilo.Canal, "INTERNO", StringComparison.OrdinalIgnoreCase))
                idHilo = null;
            else
                await conversaciones.MarcarAvisoSistemaAsync(idHilo.Value, aviso, ct);
        }

        idHilo ??= await conversaciones.CreateInternalThreadAsync(new ConversacionCrearHiloInternoRequest
        {
            NombreHilo = nombreHilo,
            AvisoSistema = aviso,
            UsuarioAccion = NombreHilo,
            SistemaAccion = SistemaAccion
        }, ct);

        await conversaciones.SendMessageAsync(new ConversacionSendMessageRequest
        {
            IdConversacion = idHilo.Value,
            Texto = texto,
            UsuarioAccion = NombreHilo,
            SistemaAccion = SistemaAccion
        }, ct);

        return idHilo.Value;
    }
}
