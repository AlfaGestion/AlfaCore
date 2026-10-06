using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IAvisosPushNotifier
{
    /// <summary>
    /// Manda un push de aviso al usuario vinculado al técnico (V_TA_Tecnicos.UsuarioAsociado), salvo que
    /// sea el mismo usuario que hizo la acción. Nunca lanza: un fallo de push no corta la operación.
    /// </summary>
    Task NotificarTecnicoAsync(string? idTecnico, string titulo, string cuerpo, string url, CancellationToken ct = default);
}

/// <summary>
/// Push de los avisos de la campana en el momento en que ocurren (2026-10-06): reserva pública nueva,
/// evento agendado por otra persona y ticket asignado. Los recordatorios por fecha (guardias, próximas
/// reuniones) se ven en la campana al entrar.
/// </summary>
public sealed class AvisosPushNotifier(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    INotificacionesPushService pushService,
    IAppEventService appEvents) : IAvisosPushNotifier
{
    // Igual que el resto de los servicios: la reserva pública entra sin sesión y usa la base por defecto.
    private string ConnectionString => sessionService.GetConnectionString() is { Length: > 0 } cs
        ? cs
        : configuration.GetConnectionString("AlfaGestion") ?? string.Empty;

    public async Task NotificarTecnicoAsync(string? idTecnico, string titulo, string cuerpo, string url, CancellationToken ct = default)
    {
        var tecnico = (idTecnico ?? string.Empty).Trim();
        if (tecnico.Length == 0)
            return;

        try
        {
            var usuario = await UsuarioDelTecnicoAsync(tecnico, ct);
            if (string.IsNullOrWhiteSpace(usuario)
                || string.Equals(usuario.Trim(), appUserSession.GetCurrentUserName(string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                return;

            await pushService.NotifyAvisoAsync([usuario.Trim()], titulo, cuerpo, url, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync("Avisos", "PushAviso", ex, "No se pudo enviar el push del aviso.",
                new { tecnico, titulo }, AppEventSeverity.Warning, ct);
        }
    }

    private async Task<string> UsuarioDelTecnicoAsync(string idTecnico, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.V_TA_Tecnicos') IS NOT NULL
                SELECT TOP (1) LTRIM(RTRIM(ISNULL(UsuarioAsociado, '')))
                FROM dbo.V_TA_Tecnicos
                WHERE LTRIM(RTRIM(IdTecnico)) = @IdTecnico AND ISNULL(Baja, 0) = 0;
            """, cn);
        cmd.Parameters.AddWithValue("@IdTecnico", idTecnico);
        return (await cmd.ExecuteScalarAsync(ct) as string) ?? string.Empty;
    }
}
