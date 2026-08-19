namespace AlfaCore.Models;

public sealed class PortalClienteRecuperarClaveRequestDto
{
    public string Identificador { get; set; } = string.Empty;
    public string? IdWeb { get; set; }
    public int? IdBase { get; set; }
    public string UrlBaseRestablecer { get; set; } = string.Empty;
    public string NombreEmpresa { get; set; } = string.Empty;
    public string? LogoUrlAbsoluta { get; set; }
}

public sealed class PortalClienteRecuperarClaveResultDto
{
    public bool RequiereCodigoCliente { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}

public sealed class PortalClienteValidarTokenResultDto
{
    public bool Valido { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}

public sealed class PortalClienteRestablecerClaveRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string NuevaClave { get; set; } = string.Empty;
    public string ConfirmarClave { get; set; } = string.Empty;
}

public sealed class PortalClienteRestablecerClaveResultDto
{
    public bool Exito { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}
