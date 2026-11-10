namespace AlfaCore.Configuration;

/// <summary>
/// Conexión guiada de Instagram y Facebook Messenger con la app central de ALFA NET (2026-11-05).
/// Es global del servidor, no de cada cliente: el cliente solo toca "Conectar" y entra con su cuenta.
/// Los secretos (App Secret) van en el appsettings del servidor, nunca en el repo ni en las bases de
/// los clientes. Instagram y Facebook son independientes: un cliente puede conectar uno solo.
/// </summary>
public sealed class MetaCanalesSocialesOptions
{
    public const string SectionName = "MetaCanalesSociales";

    /// <summary>Muestra los botones "Conectar Instagram" / "Conectar Facebook". Apagado por defecto.</summary>
    public bool Enabled { get; set; }

    /// <summary>App de Meta (la misma de WhatsApp). Vacío = usa WhatsAppEmbeddedSignup:AppId.</summary>
    public string FacebookAppId { get; set; } = string.Empty;

    /// <summary>Vacío = usa WhatsAppEmbeddedSignup:AppSecret.</summary>
    public string FacebookAppSecret { get; set; } = string.Empty;

    /// <summary>"ID de la app de Instagram" que muestra Meta en Instagram → Configuración de la API con inicio de sesión de Instagram (no es el App ID de Meta).</summary>
    public string InstagramAppId { get; set; } = string.Empty;

    /// <summary>"Clave secreta de la app de Instagram" de esa misma pantalla.</summary>
    public string InstagramAppSecret { get; set; } = string.Empty;

    public string GraphApiVersion { get; set; } = "v23.0";

    /// <summary>Permisos que se piden al conectar Facebook. Se pueden ajustar sin tocar código si Meta los cambia.</summary>
    public string FacebookScopes { get; set; } = "pages_show_list,pages_manage_metadata,pages_messaging,pages_read_engagement,business_management";

    public string InstagramScopes { get; set; } = "instagram_business_basic,instagram_business_manage_messages";

    /// <summary>
    /// Base pública HTTPS para armar la URL de vuelta de Meta. Vacío = el mismo host desde el que el
    /// usuario abrió la pantalla (así la vuelta cae en el mismo servidor). Cada host usado tiene que
    /// estar cargado en Meta como URL de redireccionamiento válida.
    /// </summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;

    /// <summary>Verify Token de los webhooks centrales de Instagram y Messenger (se carga igual en Meta).</summary>
    public string WebhookVerifyToken { get; set; } = string.Empty;

    /// <summary>Días antes del vencimiento en que se renueva el token de Instagram (dura 60 días).</summary>
    public int InstagramRenovarDiasAntes { get; set; } = 20;

    public string ResolveFacebookAppId(WhatsAppEmbeddedSignupOptions whatsApp)
        => string.IsNullOrWhiteSpace(FacebookAppId) ? (whatsApp.AppId ?? string.Empty).Trim() : FacebookAppId.Trim();

    public string ResolveFacebookAppSecret(WhatsAppEmbeddedSignupOptions whatsApp)
        => string.IsNullOrWhiteSpace(FacebookAppSecret) ? (whatsApp.AppSecret ?? string.Empty).Trim() : FacebookAppSecret.Trim();

    public bool InstagramListo
        => Enabled && !string.IsNullOrWhiteSpace(InstagramAppId) && !string.IsNullOrWhiteSpace(InstagramAppSecret);

    public bool FacebookListo(WhatsAppEmbeddedSignupOptions whatsApp)
        => Enabled && !string.IsNullOrWhiteSpace(ResolveFacebookAppId(whatsApp)) && !string.IsNullOrWhiteSpace(ResolveFacebookAppSecret(whatsApp));
}
