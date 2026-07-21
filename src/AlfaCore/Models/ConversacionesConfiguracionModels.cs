namespace AlfaCore.Models;

public sealed class ConversacionWhatsAppConfigDto
{
    public string VerifyToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v22.0";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/whatsapp/webhook";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForSend =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(PhoneNumberId);

    public bool IsConfiguredForVerify =>
        !string.IsNullOrWhiteSpace(VerifyToken);

    public bool IsReadyForMetaSetup =>
        IsConfiguredForSend &&
        IsConfiguredForVerify &&
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/whatsapp/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}

public sealed class ConversacionInstagramConfigDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string InstagramAccountId { get; set; } = string.Empty;
    public string FacebookPageId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v22.0";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/instagram/webhook";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForSend =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(InstagramAccountId);

    public bool IsConfiguredForVerify =>
        !string.IsNullOrWhiteSpace(VerifyToken);

    public bool IsReadyForMetaSetup =>
        IsConfiguredForSend &&
        IsConfiguredForVerify &&
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/instagram/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}
