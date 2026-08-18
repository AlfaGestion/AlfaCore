using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IWhatsAppWebSessionService
{
    Task<ConversacionWhatsAppNumeroDto> StartSessionAsync(int idNumero, bool includeTextCode, CancellationToken ct = default);
    Task<ConversacionWhatsAppNumeroDto> RefreshSessionAsync(int idNumero, CancellationToken ct = default);
    Task<ConversacionWhatsAppNumeroDto> StopSessionAsync(int idNumero, CancellationToken ct = default);
    Task<ConversacionWhatsAppWebSendResultDto> SendTextAsync(int? idNumeroWhatsApp, string phone, string text, string? replyToMessageId, CancellationToken ct = default);
}
