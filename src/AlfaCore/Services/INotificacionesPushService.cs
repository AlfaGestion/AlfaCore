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
    Task<bool> UserCanUseConversacionesAsync(string userName, CancellationToken ct = default);
}
