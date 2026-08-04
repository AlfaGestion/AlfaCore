using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAlfaKnowledgeSuggestionService
{
    bool IsConfigured { get; }

    string FullChatUrl { get; }

    string GetCitationUrl(AlfaKnowledgeSuggestionCitation citation);

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
        string? instruction = null,
        IReadOnlyList<AlfaKnowledgeAssistantMessage>? assistantHistory = null,
        AlfaKnowledgeImageInput? image = null,
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
