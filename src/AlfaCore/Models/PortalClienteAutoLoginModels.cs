namespace AlfaCore.Models;

public sealed class PortalClienteGenerarAutoLoginRequestDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string IdWeb { get; set; } = string.Empty;
    public int IdBase { get; set; }
    public string UrlBasePortal { get; set; } = string.Empty;
    /// <summary>Vigencia del enlace en minutos. Por defecto 60 (invitaciones y chat); un reclamo por email necesita días.</summary>
    public int ExpiracionMinutos { get; set; } = 60;
    /// <summary>
    /// false (default): un solo uso. true: se puede abrir varias veces hasta que venza; sirve para emails, donde
    /// los antivirus/vistas previas abren el enlace antes que la persona y lo quemarían.
    /// </summary>
    public bool Reutilizable { get; set; }
}

public sealed class PortalClienteAutoLoginResultDto
{
    public bool Exito { get; init; }
    public string Mensaje { get; init; } = string.Empty;
    public CatalogosClienteSessionInfo? Sesion { get; init; }
}
