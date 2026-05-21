namespace AlfaCore.Configuration;

public sealed class PushNotificationsOptions
{
    public const string SectionName = "PushNotifications";

    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = "mailto:soporte@alfanet.com.ar";

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(PublicKey)
           && !string.IsNullOrWhiteSpace(PrivateKey)
           && !string.IsNullOrWhiteSpace(Subject);
}
