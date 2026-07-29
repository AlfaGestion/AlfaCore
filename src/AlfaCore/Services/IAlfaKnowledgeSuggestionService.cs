using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAlfaKnowledgeSuggestionService
{
    /// <summary>
    /// Pide a AlfaKnowledge una sugerencia de respuesta para el último mensaje del cliente en
    /// una conversación. Devuelve <c>null</c> (nunca lanza) si AlfaKnowledge no está configurado
    /// o la llamada falla — el llamador debe tratar eso como "sugerencia no disponible ahora",
    /// nunca como un error que interrumpa la atención al cliente.
    /// </summary>
    Task<AlfaKnowledgeSuggestionResult?> SuggestReplyAsync(
        string customerMessage,
        IReadOnlyList<ConversacionMensajeDto> recentMessages,
        long conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra si el técnico usó o descartó una sugerencia — es lo que permite medir, con el
    /// tiempo, cuándo las sugerencias son lo bastante confiables como para evaluar automatizar
    /// la respuesta. Nunca lanza: un fallo acá no debe afectar la atención al cliente.
    /// </summary>
    Task SendFeedbackAsync(Guid interactionId, bool isHelpful, CancellationToken cancellationToken = default);
}
