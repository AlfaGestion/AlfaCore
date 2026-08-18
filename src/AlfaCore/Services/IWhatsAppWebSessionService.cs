using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IWhatsAppWebSessionService
{
    Task<ConversacionWhatsAppConfigDto> StartSessionAsync(bool includeTextCode, CancellationToken ct = default);
    Task<ConversacionWhatsAppConfigDto> RefreshSessionAsync(CancellationToken ct = default);
    Task<ConversacionWhatsAppConfigDto> StopSessionAsync(CancellationToken ct = default);
    Task<ConversacionWhatsAppWebSendResultDto> SendTextAsync(string phone, string text, string? replyToMessageId, CancellationToken ct = default);
}
