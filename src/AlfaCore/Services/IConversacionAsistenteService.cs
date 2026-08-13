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
}

public sealed class ConversacionAsistenteRespuesta
{
    /// <summary>Si el asistente pudo resolver la consulta (según la política); si no, se escala.</summary>
    public bool PuedeResponder { get; set; }

    public string Respuesta { get; set; } = string.Empty;
}
