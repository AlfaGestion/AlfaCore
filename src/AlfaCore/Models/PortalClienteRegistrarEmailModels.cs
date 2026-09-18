namespace AlfaCore.Models;

public sealed class PortalClienteGenerarEnlaceEmailRequestDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string IdWeb { get; set; } = string.Empty;
    public int IdBase { get; set; }
    public string UrlBaseRegistro { get; set; } = string.Empty;
}

public sealed class PortalClienteRegistrarEmailRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NuevaClave { get; set; } = string.Empty;
    public string ConfirmarClave { get; set; } = string.Empty;
}

public sealed class PortalClienteRegistrarEmailResultDto
{
    public bool Exito { get; init; }
    public string Mensaje { get; init; } = string.Empty;
}
