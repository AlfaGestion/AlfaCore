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

    /// <summary>
    /// Prioridad de atención por clasificación de cliente (CLASIFICA1/2/3 en TA_CONFIGURACION —
    /// las mismas claves sin prefijo que ya graba Desktop, no son exclusivas de Conversaciones).
    /// </summary>
    Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(CancellationToken ct = default);
    Task SavePrioridadConfigAsync(ConversacionPrioridadConfigDto config, CancellationToken ct = default);

    /// <summary>Catálogo de dbo.TA_CLASIFICACIONES, para poblar los combos de prioridad.</summary>
    Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(CancellationToken ct = default);

    /// <summary>Usuarios del sistema actual (dbo.TA_USUARIOS), para poblar checklists/multi-selects.</summary>
    Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(CancellationToken ct = default);

    /// <summary>Números de WhatsApp configurados y, para cada uno, los usuarios vinculados.</summary>
    Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(CancellationToken ct = default);
    Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default);

    /// <summary>Usuarios marcados como administradores de Conversaciones (ven/responden por cualquier número).</summary>
    Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(CancellationToken ct = default);
    Task SaveConversacionAdministradoresAsync(IReadOnlyList<string> usuarios, CancellationToken ct = default);
}
