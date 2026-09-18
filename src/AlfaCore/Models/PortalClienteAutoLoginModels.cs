namespace AlfaCore.Models;

public sealed class PortalClienteGenerarAutoLoginRequestDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string IdWeb { get; set; } = string.Empty;
    public int IdBase { get; set; }
    public string UrlBasePortal { get; set; } = string.Empty;
}

public sealed class PortalClienteAutoLoginResultDto
{
    public bool Exito { get; init; }
    public string Mensaje { get; init; } = string.Empty;
    public CatalogosClienteSessionInfo? Sesion { get; init; }
}
