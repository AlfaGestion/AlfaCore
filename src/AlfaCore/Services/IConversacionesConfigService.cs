using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConversacionesConfigService
{
    Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default);
    Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default);
    Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto config, CancellationToken ct = default);
    Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default);
    Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto config, CancellationToken ct = default);
}
