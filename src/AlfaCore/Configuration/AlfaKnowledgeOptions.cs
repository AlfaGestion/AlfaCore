namespace AlfaCore.Configuration;

/// <summary>
/// Parámetros efectivos de conexión con AlfaKnowledge para pedir sugerencias de respuesta con IA
/// en el módulo Conversaciones. En producción se resuelven desde TA_CONFIGURACION de la base
/// activa, no desde appsettings.json ni variables de entorno.
/// </summary>
public sealed class AlfaKnowledgeOptions
{
    public const string SectionName = "AlfaKnowledge";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    /// <summary>Instrucciones curadas del asistente (persona/tono/reglas). Se anteponen a la sugerencia.</summary>
    public string Instrucciones { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Últimos mensajes de la charla que se le mandan a AlfaKnowledge (CONV_ALFAKNOWLEDGE_MENSAJES_CONTEXTO).
    /// Con 60, ALFANET tardaba 31 s y AlfaCore cortaba a los 30: el bot se quedaba sin el conocimiento
    /// (2026-11-26). Con 6 a 8 tarda unos 17 s y la búsqueda se centra en lo que se pregunta.
    /// </summary>
    public int MensajesContexto { get; set; } = DefaultMensajesContexto;

    public const int DefaultMensajesContexto = 8;

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
