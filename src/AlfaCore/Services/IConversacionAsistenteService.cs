using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Asistente IA autocontenido de Conversaciones (estilo "proyecto de GPT"): responde consultas
/// usando el comportamiento y la información del negocio configurados, llamando directo a OpenAI.
/// No depende de AlfaKnowledge (Fase 1: información pegada; Fase 2 sumará archivos/RAG).
/// </summary>
public interface IConversacionAsistenteService
{
    bool IsConfigured { get; }

    Task<ConversacionAsistenteRespuesta?> ResponderAsync(
        string comportamiento,
        string informacion,
        string politica,
        string mensajeCliente,
        IReadOnlyList<ConversacionMensajeDto> historial,
        bool fueraDeHorario = false,
        bool esUrgente = false,
        string? conocimientoBase = null,
        string? contextoCliente = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resumen genérico con IA (salida en texto plano): se le pasan instrucciones (system) y el
    /// contenido a resumir (user). Devuelve null si no hay API key o si falla la llamada.
    /// </summary>
    Task<string?> ResumirAsync(string instrucciones, string contenido, CancellationToken ct = default);
}

public sealed class ConversacionAsistenteRespuesta
{
    /// <summary>Si el asistente pudo resolver la consulta (según la política); si no, se escala.</summary>
    public bool PuedeResponder { get; set; }

    public string Respuesta { get; set; } = string.Empty;

    /// <summary>
    /// Qué representa la respuesta, para que el cliente nunca quede sin contestación:
    /// RESUELVE (la resolvió), ACLARA (repregunta para poder resolver) o DERIVA (contención +
    /// pasa a un humano).
    /// </summary>
    public string Tipo { get; set; } = "RESUELVE";
}
