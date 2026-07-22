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
    Task<IReadOnlyList<ConversacionClienteCandidateDto>> SearchClientesParaRelacionarAsync(string texto, CancellationToken ct = default);
    Task RelacionarClienteAsync(ConversacionRelacionarClienteRequest request, CancellationToken ct = default);
    Task RenameConversationAsync(ConversacionRenameRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionTypingDto>> GetTypingAsync(long conversationId, string? clienteIdActual = null, CancellationToken ct = default);
    Task SetTypingAsync(ConversacionTypingRequest request, CancellationToken ct = default);
    Task<ConversacionMessageResultDto> SendMessageAsync(ConversacionSendMessageRequest request, CancellationToken ct = default);
    Task<ConversacionMessageResultDto> SendReactionAsync(ConversacionReaccionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesAsync(ConversacionPlantillaFilters filters, CancellationToken ct = default);
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
    Task SetConversationPinAsync(long idConversacion, string usuario, string? sistema, bool fijada, CancellationToken ct = default);
    Task MarkConversationReadAsync(long idConversacion, string usuario, string? sistema, CancellationToken ct = default);
    Task<ConversacionWebhookResultDto> RegisterIncomingWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default);
    Task<ConversacionWebhookResultDto> RegisterIncomingInstagramWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default);
    Task<long> CreateInternalThreadAsync(ConversacionCrearHiloInternoRequest request, CancellationToken ct = default);
    Task<ConversacionCrearWhatsAppResultDto> CreateOrGetWhatsAppConversationAsync(ConversacionCrearWhatsAppRequest request, CancellationToken ct = default);
    Task<ConversacionAdjuntoDto> UploadAttachmentAsync(ConversacionUploadAdjuntoRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionStickerFavoritoDto>> GetFavoriteStickersAsync(CancellationToken ct = default);
    Task SaveFavoriteStickerAsync(long idAdjunto, CancellationToken ct = default);
    Task<ConversacionAdjuntoDto> SendFavoriteStickerAsync(long idConversacion, long idFavorito, string? idTecnicoAutor = null, string? usuarioAccion = null, string? sistemaAccion = null, CancellationToken ct = default);
    Task<IReadOnlyList<ConversacionAdjuntoDto>> GetConversationAttachmentsAsync(long idConversacion, CancellationToken ct = default);
    Task<ConversacionAdjuntosRecoveryResultDto> RecoverConversationAttachmentsAsync(long idConversacion, CancellationToken ct = default);
    Task<ConversacionAdjuntoServeDto?> GetAttachmentForServeAsync(long idAdjunto, int? idBase = null, bool includeDownloadName = true, CancellationToken ct = default);
    string GetAttachmentScopeKey();
}
