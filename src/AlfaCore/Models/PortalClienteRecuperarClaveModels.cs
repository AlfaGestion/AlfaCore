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

// Usado por la invitación al Portal Cliente (ficha del cliente) para generar el enlace de
// "crear/cambiar contraseña" sin pasar por el flujo de "¿Olvidaste tu contraseña?" (que exige
// buscar al cliente por identificador). El código de cliente ya es conocido por el llamador.
public sealed class PortalClienteGenerarEnlaceRequestDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string? IdWeb { get; set; }
    public int? IdBase { get; set; }
    public string UrlBaseRestablecer { get; set; } = string.Empty;
}
