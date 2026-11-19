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
    /// <summary>Reescribe un borrador según una instrucción (tono, traducción). Devuelve solo el texto final.</summary>
    Task<string?> TransformarTextoAsync(string texto, string instruccion, CancellationToken ct = default);
    /// <summary>
    /// Redacta un texto nuevo con un prompt de sistema propio (informes, borradores). Null si no hay IA o falla.
    /// <paramref name="modelo"/> reemplaza a OPENAI_MODEL solo para esta llamada.
    /// </summary>
    Task<string?> GenerarTextoAsync(string systemPrompt, string contenido, string funcion, int timeoutSegundos = 90, string? modelo = null, CancellationToken ct = default);
}
