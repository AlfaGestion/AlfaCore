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

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
