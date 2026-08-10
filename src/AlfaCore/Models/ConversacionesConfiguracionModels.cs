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

/// <summary>
/// Qué código de dbo.TA_CLASIFICACIONES cuenta como prioridad 1 (más importante), 2 o 3 para
/// ordenar la cola de espera. Un cliente sin clasificación, o con una que no está en ninguna de
/// las 3, cae en "el resto" (prioridad 4, la más baja). Se guarda en dbo.TA_CONFIGURACION con las
/// claves CLASIFICA1/2/3 — las mismas, sin prefijo, que ya usa Desktop, no son propias de este
/// módulo.
/// </summary>
public sealed record ConversacionClasificacionOptionDto(string Codigo, string Descripcion);

public sealed class ConversacionPrioridadConfigDto
{
    public string Clasifica1 { get; set; } = string.Empty;
    public string Clasifica2 { get; set; } = string.Empty;
    public string Clasifica3 { get; set; } = string.Empty;
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

