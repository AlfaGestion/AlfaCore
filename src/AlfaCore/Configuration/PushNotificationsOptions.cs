namespace AlfaCore.Configuration;

public sealed class PushNotificationsOptions
{
    public const string SectionName = "PushNotifications";

    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = "mailto:admin@alfagestion.com";

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(PublicKey)
           && !string.IsNullOrWhiteSpace(PrivateKey)
           && !string.IsNullOrWhiteSpace(Subject);

    public bool HasPublicKey => !string.IsNullOrWhiteSpace(PublicKey);
    public bool HasPrivateKey => !string.IsNullOrWhiteSpace(PrivateKey);
    public bool HasSubject => !string.IsNullOrWhiteSpace(Subject);

    public string GetConfigurationMessage()
    {
        var missing = new List<string>();
        if (!HasPublicKey)
            missing.Add("PushNotifications:PublicKey");
        if (!HasPrivateKey)
            missing.Add("PushNotifications:PrivateKey");
        if (!HasSubject)
            missing.Add("PushNotifications:Subject");

        return missing.Count == 0
            ? "Configuración Web Push completa."
            : $"El servidor todavía no tiene configuradas las claves push. Configurá {string.Join(" y ", missing)}.";
    }
}
