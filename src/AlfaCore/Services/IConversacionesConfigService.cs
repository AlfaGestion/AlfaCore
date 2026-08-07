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
    Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(CancellationToken ct = default);
    Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default);
    /// <summary>
    /// Igual que <see cref="SaveAlfaKnowledgeConfigAsync"/> pero escribe contra una base de un
    /// cliente puntual en vez de la base activa de la sesión actual — para cuando un superadmin
    /// aprovisiona AlfaKnowledge para un cliente distinto al que tiene abierto (ver
    /// <c>CentralAdminService.ActivarModuloAsync</c>).
    /// </summary>
    Task SaveAlfaKnowledgeConfigForConnectionAsync(string connectionString, ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default);
    Task<ConversacionAlfaKnowledgeConnectionTestResultDto> TestAlfaKnowledgeConnectionAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default);
    Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(CancellationToken ct = default);
    Task SaveAutomatizacionesConfigAsync(ConversacionAutomatizacionesConfigDto config, CancellationToken ct = default);
}
