using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAlfaKnowledgeSuggestionService
{
    bool IsConfigured { get; }

    string FullChatUrl { get; }

    string GetCitationUrl(AlfaKnowledgeSuggestionCitation citation);

    /// <summary>
    /// Igual que <see cref="GetCitationUrl"/>, pero devuelve <c>null</c> salvo que la cita apunte a
    /// una URL pública real (http/https). GetCitationUrl siempre devuelve algo -- para un archivo
    /// interno o sin URL propia, cae a un link DENTRO de AlfaKnowledge (document-viewer.html o el
    /// buscador), que requiere login y no tiene sentido mandarle a un cliente por WhatsApp. Usar
    /// este método en cualquier lugar que arme un mensaje saliente para el cliente.
    /// </summary>
    string? TryGetPublicCitationUrl(AlfaKnowledgeSuggestionCitation citation);

    /// <summary>
    /// Pide a AlfaKnowledge una sugerencia de respuesta o una asistencia guiada por la instrucción
    /// del técnico. Devuelve <c>null</c> (nunca lanza) si AlfaKnowledge no está configurado o la
    /// llamada falla; el llamador debe tratarlo como asistencia no disponible, nunca como un error
    /// que interrumpa la atención al cliente.
    /// </summary>
    Task<AlfaKnowledgeSuggestionResult?> SuggestReplyAsync(
        string customerMessage,
        IReadOnlyList<ConversacionMensajeDto> recentMessages,
        long conversationId,
        string mode = AlfaKnowledgeSuggestionModes.ReplySuggestion,
        string? instruction = null,
        IReadOnlyList<AlfaKnowledgeAssistantMessage>? assistantHistory = null,
        AlfaKnowledgeImageInput? image = null,
        AlfaKnowledgeTextImprovementInput? textImprovement = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra si el técnico usó o descartó una sugerencia — es lo que permite medir, con el
    /// tiempo, cuándo las sugerencias son lo bastante confiables como para evaluar automatizar
    /// la respuesta. Nunca lanza: un fallo acá no debe afectar la atención al cliente.
    /// </summary>
    Task SendFeedbackAsync(Guid interactionId, bool isHelpful, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda en AlfaKnowledge una versión corregida de una respuesta sugerida para incorporarla
    /// al conocimiento curado. Devuelve <c>false</c> si el servicio no está disponible.
    /// </summary>
    Task<bool> SaveCorrectionAsync(
        AlfaKnowledgeCorrectionRequest request,
        CancellationToken cancellationToken = default);
}
