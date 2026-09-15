using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConversacionesConfigService
{
    Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default);
    Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default);
    Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto config, CancellationToken ct = default);
    Task<ConversacionWhatsAppConfigDto> GenerateWhatsAppWebPairingAsync(ConversacionWhatsAppWebPairingRequestDto request, CancellationToken ct = default);
    Task<ConversacionWhatsAppConfigDto> ClearWhatsAppWebPairingAsync(CancellationToken ct = default);
    Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default);
    Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto config, CancellationToken ct = default);
    Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default);
    Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto config, CancellationToken ct = default);
    Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default);
    Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default);
    Task SaveMercadoLibreTokensAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default);
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
    Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default);
    Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroByInstanceNameAsync(string instanceName, CancellationToken ct = default);
    Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default);
    Task<ConversacionWhatsAppNumeroDto> UpsertEmbeddedSignupWhatsAppNumeroForBaseAsync(
        int idBase,
        ConversacionWhatsAppNumeroDto numero,
        CancellationToken ct = default);
    Task SaveWhatsAppNumeroWebSessionAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default);

    /// <summary>
    /// Nombre del Portfolio/Business de Meta para cada MetaBusinessId conocido (base activa de la
    /// sesión) -- nunca pega contra Meta, sólo lee la caché local (dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS,
    /// self-tolerant: base sin la tabla todavía = diccionario vacío, no un error). Ids que no están en el
    /// resultado significan "portfolio conocido, nombre todavía no resuelto" -- no "sin portfolio".
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, CancellationToken ct = default);
    /// <summary>
    /// Guarda (best-effort) el nombre de un Portfolio/Business para reutilizarlo después sin volver a
    /// preguntarle a Meta. Pensado para llamarse sólo cuando el nombre YA se obtuvo de una llamada a
    /// Meta que iba a hacerse de todos modos (discovery de Embedded Signup) -- nunca dispara una llamada
    /// nueva. Nunca debe poder fallar el flujo que la llama: ver WhatsAppEmbeddedOperationalImportService.
    /// </summary>
    Task SetPortfolioNameAsync(int idBase, string metaBusinessId, string portfolioName, CancellationToken ct = default);
    /// <summary>
    /// Reserva atómicamente el derecho a intentar resolver el nombre de un Portfolio/Business contra
    /// Meta: true si nadie más lo intentó dentro de <paramref name="throttleWindow"/> y el nombre
    /// todavía no está cacheado (el llamador debe entonces llamar a Meta); false si ya hay un nombre
    /// cacheado, o si otro intento (de este proceso u otro circuito Blazor) ya reservó la ventana --
    /// en ambos casos el llamador NO debe llamar a Meta. Autocontenido: crea la fila si no existe.
    /// </summary>
    Task<bool> TryReserveResolutionAttemptAsync(int idBase, string metaBusinessId, TimeSpan throttleWindow, CancellationToken ct = default);
    /// <summary>
    /// Completa MetaBusinessId/WabaId de un número YA existente que se quedó sin esos ids (típicamente
    /// conectado antes de que existiera esta funcionalidad) -- nunca sobrescribe un valor ya presente.
    /// Usa la base activa de la sesión, igual que GetWhatsAppNumerosAsync (el llamador sólo backfillea
    /// números de la base que tiene abierta). Best-effort: nunca lanza.
    /// </summary>
    Task BackfillNumeroMetaIdentityAsync(int idNumero, string metaBusinessId, string wabaId, CancellationToken ct = default);

    /// <summary>Usuarios marcados como administradores de Conversaciones (ven/responden por cualquier número).</summary>
    Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(CancellationToken ct = default);
    Task SaveConversacionAdministradoresAsync(IReadOnlyList<string> usuarios, CancellationToken ct = default);
    Task<ConversacionesInboxPreferenceDto> GetInboxPreferenceAsync(string userName, string? sistema, CancellationToken ct = default);
    Task SaveInboxPreferenceAsync(string userName, string? sistema, ConversacionesInboxPreferenceDto preference, CancellationToken ct = default);

    /// <summary>Reglas del motor de palabras clave (CONV_REGLAS), ordenadas por Orden.</summary>
    Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(CancellationToken ct = default);
    Task<int> SaveReglaAsync(ConversacionReglaDto regla, CancellationToken ct = default);
    Task DeleteReglaAsync(int idRegla, CancellationToken ct = default);
}
