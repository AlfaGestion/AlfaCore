using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConversacionesService
{
    Task<bool> HasConversationSchemaAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionTecnicoOptionDto>> GetTechniciansAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionEstadoOptionDto>> GetStatesAsync(CancellationToken ct = default);
    Task<ConversacionesEstadisticasDto> GetEstadisticasAsync(ConversacionesEstadisticasFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionInboxItemDto>> GetInboxAsync(ConversacionesInboxFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionAuditoriaMensajeDto>> GetAuditMessagesAsync(ConversacionesInboxFilters filters, CancellationToken ct = default);
    Task<ConversacionDetalleDto?> GetConversationAsync(long conversationId, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionMensajeDto>> GetMessagesAsync(long conversationId, CancellationToken ct = default);
    Task<ConversacionMensajesPaginaDto> GetMessagesPageAsync(long conversationId, int take, DateTime? beforeDate = null, long? beforeMessageId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionClienteCandidateDto>> SearchClientesParaRelacionarAsync(string texto, CancellationToken ct = default);
    Task RelacionarClienteAsync(ConversacionRelacionarClienteRequest request, CancellationToken ct = default);
    Task RenameConversationAsync(ConversacionRenameRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionTypingDto>> GetTypingAsync(long conversationId, string? clienteIdActual = null, CancellationToken ct = default);
    Task SetTypingAsync(ConversacionTypingRequest request, CancellationToken ct = default);
    Task<ConversacionMessageResultDto> SendMessageAsync(ConversacionSendMessageRequest request, CancellationToken ct = default);
    Task<ConversacionMessageResultDto> SendReactionAsync(ConversacionReaccionRequest request, CancellationToken ct = default);
    Task SetConversationWhatsAppNumeroAsync(ConversacionWhatsAppNumeroRequest request, CancellationToken ct = default);
    /// <summary>
    /// Marca interna de seguimiento por mensaje (ej. "PENDIENTE"/"COMPLETADA") — nunca se envía
    /// al cliente por ningún canal, es solo para el agente. Pasar <c>null</c>/vacío quita la marca.
    /// </summary>
    Task SetMensajeMarcaInternaAsync(long idConversacion, long idMensaje, string? marca, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesAsync(ConversacionPlantillaFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesForConversationAsync(long idConversacion, CancellationToken ct = default);
    Task<ConversacionPlantillaDto?> GetTemplateAsync(long idPlantilla, CancellationToken ct = default);
    Task<long> SaveTemplateDraftAsync(ConversacionPlantillaSaveRequest request, CancellationToken ct = default);
    Task ArchiveTemplateAsync(long idPlantilla, CancellationToken ct = default);
    Task SubmitTemplateForApprovalAsync(ConversacionPlantillaSubmitRequest request, CancellationToken ct = default);
    Task SyncTemplateStatusAsync(long idPlantilla, CancellationToken ct = default);
    Task<ConversacionPlantillaAutoValuesDto> GetTemplateAutoValuesAsync(long idConversacion, int variableCount, CancellationToken ct = default);
    Task<ConversacionPlantillaMessageResultDto> SendTemplateMessageAsync(ConversacionPlantillaSendRequest request, CancellationToken ct = default);
    Task<long> AddInternalNoteAsync(ConversacionNotaInternaRequest request, CancellationToken ct = default);
    Task<long> AddInternalEventAsync(ConversacionEventoInternoRequest request, CancellationToken ct = default);
    Task AssignConversationAsync(ConversacionAsignacionRequest request, CancellationToken ct = default);
    Task ChangeStatusAsync(ConversacionEstadoRequest request, CancellationToken ct = default);
    /// <summary>Procesa el auto-cierre por inactividad para la base activa. Devuelve cuántas acciones hizo (avisos + cierres).</summary>
    Task<int> ProcesarAutoCierreAsync(CancellationToken ct = default);

    Task<int> ProcesarSeguimientosSlaAsync(CancellationToken ct = default);

    /// <summary>Retoma las conversaciones cuya espera configurada para el Asistente IA ("esperar N minutos
    /// antes de responder") ya se cumplió. Devuelve cuántas procesó.</summary>
    Task<int> ProcesarRespuestasBotPendientesAsync(CancellationToken ct = default);

    // Mensajes programados (envío diferido)
    Task<long> ProgramarMensajeAsync(ConversacionProgramarRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionMensajeProgramadoDto>> GetMensajesProgramadosAsync(long idConversacion, CancellationToken ct = default);
    Task CancelarMensajeProgramadoAsync(long idProgramado, CancellationToken ct = default);
    /// <summary>Momento en que se cierra la ventana de 24 h de WhatsApp (último entrante + 24 h); null si no hay entrante.</summary>
    Task<DateTime?> GetVentanaCierreWhatsAppAsync(long idConversacion, CancellationToken ct = default);
    Task<int> ProcesarMensajesProgramadosAsync(CancellationToken ct = default);
    Task SetConversationPinAsync(long idConversacion, string usuario, string? sistema, bool fijada, CancellationToken ct = default);
    Task MarkConversationReadAsync(long idConversacion, string usuario, string? sistema, CancellationToken ct = default);
    Task<ConversacionWebhookResultDto> RegisterIncomingWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default);
    Task RegisterIncomingWhatsAppWebMessageAsync(ConversacionWhatsAppWebIncomingMessageDto request, CancellationToken ct = default);
    Task<ConversacionWebhookResultDto> RegisterIncomingInstagramWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default);
    Task<ConversacionWebhookResultDto> RegisterIncomingFacebookWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default);
    Task<ConversacionWebhookResultDto> RegisterIncomingMercadoLibreWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default);
    Task<ConversacionWebhookResultDto> SyncMercadoLibreQuestionsAsync(CancellationToken ct = default);
    /// <summary>
    /// Contexto del cliente asociado a la conversación (N° de cliente, razón social, rubro del
    /// negocio y prioridad P1-P4 según su clasificación), como texto listo para anteponer a la
    /// sugerencia del copiloto. Devuelve cadena vacía si la conversación no tiene cliente asociado.
    /// </summary>
    Task<string> GetContextoClienteAsistenteAsync(long idConversacion, CancellationToken ct = default);
    Task<long> CreateInternalThreadAsync(ConversacionCrearHiloInternoRequest request, CancellationToken ct = default);
    Task<ConversacionCrearWhatsAppResultDto> CreateOrGetWhatsAppConversationAsync(ConversacionCrearWhatsAppRequest request, CancellationToken ct = default);
    Task<int> ProcessWhatsAppWebInboxAsync(CancellationToken ct = default);

    Task<int> ProcessWhatsAppWebAcksAsync(CancellationToken ct = default);
    Task<ConversacionAdjuntoDto> UploadAttachmentAsync(ConversacionUploadAdjuntoRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionStickerFavoritoDto>> GetFavoriteStickersAsync(CancellationToken ct = default);
    Task SaveFavoriteStickerAsync(long idAdjunto, CancellationToken ct = default);
    Task<ConversacionAdjuntoDto> SendFavoriteStickerAsync(long idConversacion, long idFavorito, string? idTecnicoAutor = null, string? usuarioAccion = null, string? sistemaAccion = null, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionAdjuntoDto>> GetConversationAttachmentsAsync(long idConversacion, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionAdjuntoDto>> GetMessageAttachmentsAsync(long idConversacion, IReadOnlyCollection<long> messageIds, CancellationToken ct = default);
    Task<ConversacionAdjuntosRecoveryResultDto> RecoverConversationAttachmentsAsync(long idConversacion, CancellationToken ct = default);
    Task<ConversacionAdjuntoServeDto?> GetAttachmentForServeAsync(long idAdjunto, int? idBase = null, bool includeDownloadName = true, CancellationToken ct = default);
    string GetAttachmentScopeKey();
}
