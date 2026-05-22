namespace AlfaCore.Models;

public static class NotificacionesPushScopes
{
    public const string Asignadas = "asignadas";
    public const string Accesibles = "accesibles";
}

public sealed class NotificacionesPushPreferencesDto
{
    public bool Habilitadas { get; set; }
    public string Alcance { get; set; } = NotificacionesPushScopes.Asignadas;
    public List<string> Canales { get; set; } = ["WHATSAPP"];
}

public sealed class NotificacionesPushSubscriptionDto
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public class NotificacionesPushDeviceRequest
{
    public string DeviceId { get; set; } = string.Empty;
}

public sealed class NotificacionesPushRegistrationRequest : NotificacionesPushDeviceRequest
{
    public NotificacionesPushSubscriptionDto Subscription { get; set; } = new();
    public string UserAgent { get; set; } = string.Empty;
}

public sealed class NotificacionesPushPreferencesRequest : NotificacionesPushDeviceRequest
{
    public NotificacionesPushPreferencesDto Preferences { get; set; } = new();
}

public sealed class NotificacionesPushClientSettingsDto
{
    public bool Configurado { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    public bool PublicKeyConfigurada { get; set; }
    public bool PrivateKeyConfigurada { get; set; }
    public bool SubjectConfigurado { get; set; }
    public string ConfiguracionMensaje { get; set; } = string.Empty;
    public NotificacionesPushPreferencesDto Preferences { get; set; } = new();
}

public sealed class NotificacionesPushSendResultDto
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public List<NotificacionesPushEndpointResultDto> Results { get; set; } = [];
}

public sealed class NotificacionesPushEndpointResultDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string EndpointParcial { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string Error { get; set; } = string.Empty;
    public string ResponseBody { get; set; } = string.Empty;
}

public sealed class NotificacionesPushDiagnosticsDto
{
    public string UserName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public int SubscriptionCount { get; set; }
    public int ActiveSubscriptionCount { get; set; }
    public NotificacionesPushPreferencesDto Preferences { get; set; } = new();
    public List<NotificacionesPushEndpointResultDto> Subscriptions { get; set; } = [];
}

public sealed class NotificacionMensajeNuevoDto
{
    public long IdConversacion { get; set; }
    public long IdMensaje { get; set; }
    public string Canal { get; set; } = string.Empty;
    public string Contacto { get; set; } = string.Empty;
    public string Resumen { get; set; } = string.Empty;
    public string IdTecnico { get; set; } = string.Empty;
}
