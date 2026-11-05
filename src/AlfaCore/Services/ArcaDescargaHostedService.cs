using System.Globalization;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Descarga y control automático diario de «Mis Comprobantes» para las bases que lo activaron en Contabilidad → Control
/// contra ARCA. A partir de la hora configurada baja el mes actual (y el anterior hasta el día 10), lo compara contra
/// el sistema y deja el resumen en ALFACORE_ARCA_ULTIMA_RECIBIDOS/EMITIDOS. No graba comprobantes: la importación de
/// los faltantes se confirma desde la pantalla.
/// </summary>
public sealed class ArcaDescargaHostedService(IServiceProvider services, IConfiguration configuration, IAppModeService appMode,
    ArcaDescargaRegistro registro, ILogger<ArcaDescargaHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CicloAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Error en la descarga automática de ARCA."); }
            try { await Task.Delay(Intervalo, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task CicloAsync(CancellationToken ct)
    {
        var ids = registro.Bases();
        if (ids.Count == 0) return;
        if (appMode.IsSaaSMode && !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
        {
            IReadOnlyList<BaseCentralDto> bases;
            using (var scope = services.CreateScope())
            {
                if (!await scope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>().DebeEjecutarWorkersAsync(ct)) return;
                bases = await scope.ServiceProvider.GetRequiredService<ICentralBasesService>().GetAllAsync(ct);
            }
            foreach (var b in bases.Where(b => ids.Contains(b.IdBase)))
            {
                ct.ThrowIfCancellationRequested();
                using var scope = services.CreateScope();
                var session = scope.ServiceProvider.GetRequiredService<ISessionService>();
                session.SetWebhookOverride(new SessionDto
                {
                    BaseId = b.IdBase, Nombre = b.Nombre, Servidor = b.DbServer, BaseDatos = b.DbName,
                    Usuario = b.DbUser, Password = b.DbPassword, TrustServerCertificate = true
                });
                await BaseAsync(scope.ServiceProvider.GetRequiredService<ArcaServidorService>().Estado(b.IdBase), session.GetConnectionString(), b.Nombre, ct);
            }
        }
        else
        {
            using var scope = services.CreateScope();
            await BaseAsync(scope.ServiceProvider.GetRequiredService<ArcaServidorService>().Estado(0), scope.ServiceProvider.GetRequiredService<ISessionService>().GetConnectionString(), "", ct);
        }
    }

    private async Task BaseAsync(ArcaServidorEstado servidor, string connectionString, string nombre, CancellationToken ct)
    {
        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            var c = await ArcaDescargaService.LeerConfigAsync(cn, servidor, ct);
            if (!TimeSpan.TryParseExact(c.HoraAuto, @"hh\:mm", CultureInfo.InvariantCulture, out var hora) || DateTime.Now.TimeOfDay < hora) return;
            foreach (var recibidos in new[] { true, false })
            {
                if (!(recibidos ? c.AutoRecibidos : c.AutoEmitidos)) continue;
                var ultima = recibidos ? c.UltimaAutoRecibidos : c.UltimaAutoEmitidos;
                if (ultima.StartsWith(DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)) continue;
                var clave = recibidos ? ArcaDescargaService.ClaveUltimaRecibidos : ArcaDescargaService.ClaveUltimaEmitidos;
                var hoy = DateTime.Today;
                var periodos = hoy.Day <= 10 ? new[] { hoy.AddMonths(-1), hoy } : new[] { hoy };
                var partes = new List<string>();
                foreach (var p in periodos)
                {
                    try
                    {
                        var archivo = await ArcaDescargaService.DescargarEnConexionAsync(cn, servidor, recibidos, p.Year, p.Month, visible: false, ct);
                        var bytes = await File.ReadAllBytesAsync(Path.Combine(servidor.Carpeta, archivo.Nombre), ct);
                        var r = await ControlArcaService.ControlarEnConexionAsync(cn, ControlArcaService.Leer(bytes, archivo.Nombre), ct);
                        var registro = "";
                        try { registro = $" (transmisión {await ArcaTransmisionRegistro.RegistrarControlAsync(cn, "AlfaCore (automático)", archivo.Nombre, r, ct)})"; }
                        catch (Exception ex) when (ex is SqlException or InvalidOperationException) { registro = " (no se pudo registrar el historial)"; }
                        partes.Add($"{p:MM/yyyy}: {r.ComprobantesArca} en ARCA, {r.Cantidad(ControlArcaEstado.FaltaEnSistema)} faltan, " +
                                   $"{r.Cantidad(ControlArcaEstado.DifiereImporte) + r.Cantidad(ControlArcaEstado.OtroPeriodo)} con diferencias{registro}");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or SqlException)
                    {
                        // Se deja el motivo (sin datos sensibles) para verlo en la pantalla.
                        partes.Add($"{p:MM/yyyy}: error - {ex.Message}");
                    }
                }
                await ArcaDescargaService.GuardarClaveAsync(cn, clave,
                    Corto($"{DateTime.Now:dd/MM/yyyy HH:mm} · {string.Join(" · ", partes)}", 150), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "La descarga automática de ARCA falló en la base {Base}.", nombre);
        }
    }

    private static string Corto(string s, int n) => s.Length > n ? s[..n] : s;
}
