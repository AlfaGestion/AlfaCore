using AlfaCore.Models;

namespace AlfaCore.Services;

public interface INotificacionesPushService
{
    Task<NotificacionesPushClientSettingsDto> GetClientSettingsAsync(string userName, string deviceId, CancellationToken ct = default);
    Task SaveSubscriptionAsync(string userName, NotificacionesPushRegistrationRequest request, CancellationToken ct = default);
    Task DeleteSubscriptionAsync(string userName, string deviceId, CancellationToken ct = default);
    Task SavePreferencesAsync(string userName, NotificacionesPushPreferencesRequest request, CancellationToken ct = default);
    Task<NotificacionesPushSendResultDto> SendTestAsync(string userName, string deviceId, CancellationToken ct = default);
    Task<NotificacionesPushDiagnosticsDto> GetDiagnosticsAsync(string userName, string deviceId, CancellationToken ct = default);
    Task NotifyNewMessageAsync(long idConversacion, long idMensaje, CancellationToken ct = default);
    /// <summary>
    /// Aviso dirigido a usuarios puntuales (ej. "Notificar a..." en un hilo interno) — a
    /// diferencia de <see cref="NotifyNewMessageAsync"/>, no filtra por las preferencias
    /// generales de alcance/canal del usuario, es un llamado explícito.
    /// </summary>
    Task<NotificacionesPushSendResultDto> NotifyMentionAsync(long idConversacion, long idMensaje, IReadOnlyCollection<string> userNames, string mencionadoPor, CancellationToken ct = default);

    /// <summary>Push de un aviso de la campana (reserva, evento agendado, ticket asignado) a usuarios puntuales con push habilitado.</summary>
    Task<NotificacionesPushSendResultDto> NotifyAvisoAsync(IReadOnlyCollection<string> userNames, string titulo, string cuerpo, string url, CancellationToken ct = default);
    Task<bool> UserCanUseConversacionesAsync(string userName, CancellationToken ct = default);
}
