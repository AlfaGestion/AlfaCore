namespace AlfaCore.Models;

/// <summary>Identidad mínima compartida por las experiencias públicas de AlfaCore.</summary>
public sealed class CompanyBrandingDto
{
    public string Nombre { get; init; } = string.Empty;
    public string Direccion { get; init; } = string.Empty;
    public string Telefono { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string SitioWeb { get; init; } = string.Empty;
    public string LogoUrl { get; init; } = string.Empty;
    public bool TieneLogo { get; init; }
}
