using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConversacionesConfigService
{
    Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default);
    Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default);
    Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto config, CancellationToken ct = default);
    Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default);
    Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto config, CancellationToken ct = default);
    Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default);
    Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto config, CancellationToken ct = default);
    Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default);
    Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default);
}
