using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Análisis IA de una conversación (Nivel 1 del roadmap de automatizaciones): resumen,
/// intención y sentimiento. Llamada directa a OpenAI sobre el hilo (no usa AlfaKnowledge).
/// </summary>
public interface IConversacionAnalisisService
{
    bool IsConfigured { get; }
    Task<ConversacionAnalisisDto?> AnalizarAsync(IReadOnlyList<ConversacionMensajeDto> mensajes, CancellationToken ct = default);
    Task<ConversacionExtraccionDto?> ExtraerOportunidadAsync(IReadOnlyList<ConversacionMensajeDto> mensajes, CancellationToken ct = default);
}
