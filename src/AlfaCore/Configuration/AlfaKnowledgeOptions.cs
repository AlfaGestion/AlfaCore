namespace AlfaCore.Configuration;

/// <summary>
/// Conexión con AlfaKnowledge (la base de conocimiento interna) para pedir sugerencias de
/// respuesta con IA en el módulo Conversaciones. <see cref="ApiKey"/> no debe vivir en
/// appsettings.json — se completa vía variable de entorno <c>AlfaKnowledge__ApiKey</c> en
/// <c>.env</c>, mismo patrón que <c>PushNotifications__PrivateKey</c>.
/// </summary>
public sealed class AlfaKnowledgeOptions
{
    public const string SectionName = "AlfaKnowledge";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
