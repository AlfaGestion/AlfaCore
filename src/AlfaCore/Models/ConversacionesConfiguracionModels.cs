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

public sealed class ConversacionFacebookConfigDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string PageId { get; set; } = string.Empty;
    public string PageUsername { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v22.0";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/facebook/webhook";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForSend =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(PageId);

    public bool IsConfiguredForVerify =>
        !string.IsNullOrWhiteSpace(VerifyToken);

    public bool IsReadyForMetaSetup =>
        IsConfiguredForSend &&
        IsConfiguredForVerify &&
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/facebook/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}

public sealed class ConversacionMercadoLibreConfigDto
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string SellerId { get; set; } = string.Empty;
    public string SiteId { get; set; } = "MLA";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/api/conversaciones/mercadolibre/webhook";
    public string OAuthCallbackPath { get; set; } = "/api/conversaciones/mercadolibre/oauth/callback";
    public string ApiBaseUrl { get; set; } = "https://api.mercadolibre.com";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfiguredForAuth =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public bool IsConfiguredForApi =>
        IsConfiguredForAuth &&
        !string.IsNullOrWhiteSpace(AccessToken);

    public bool IsReadyForWebhook =>
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string GetWebhookUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(WebhookPath) ? "/api/conversaciones/mercadolibre/webhook" : WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }

    public string GetOAuthCallbackUrl()
    {
        var baseUrl = (PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(OAuthCallbackPath) ? "/api/conversaciones/mercadolibre/oauth/callback" : OAuthCallbackPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}{path}";
    }
}

public sealed class ConversacionAlfaKnowledgeConfigDto
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string KnowledgeBaseId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey);

    public string FullChatUrl
        => string.IsNullOrWhiteSpace(BaseUrl)
            ? string.Empty
            : BaseUrl.Trim().TrimEnd('/') + "/";
}

/// <summary>
/// Automatizaciones "Nivel 0" (sin IA, sin aprobación de operador): respuesta fija cuando llega
/// un mensaje de WhatsApp fuera del horario de atención configurado.
/// </summary>
public sealed class ConversacionAutomatizacionesConfigDto
{
    public bool Activo { get; set; }
    public string MensajeFueraHorario { get; set; } = string.Empty;
    public bool Lunes { get; set; } = true;
    public bool Martes { get; set; } = true;
    public bool Miercoles { get; set; } = true;
    public bool Jueves { get; set; } = true;
    public bool Viernes { get; set; } = true;
    public bool Sabado { get; set; }
    public bool Domingo { get; set; }
    public string HoraDesde { get; set; } = "09:00";
    public string HoraHasta { get; set; } = "18:00";
    public string ConfigSource { get; set; } = string.Empty;

    public bool IsConfigured => Activo && !string.IsNullOrWhiteSpace(MensajeFueraHorario);
}

public sealed class ConversacionAlfaKnowledgeConnectionTestResultDto
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string Service { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string DataSource { get; set; } = string.Empty;
    public string KnowledgeBase { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

