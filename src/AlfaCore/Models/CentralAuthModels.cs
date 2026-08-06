namespace AlfaCore.Models;

public sealed class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public SesionCentralDto? Session { get; init; }
    public string RedirectUrl { get; init; } = string.Empty;
    public bool RequiresBaseSelection { get; init; }
}

public sealed class UsuarioCentralDto
{
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string IdCliente { get; init; } = string.Empty;
}

public sealed class ClienteCentralDto
{
    public string IdCliente { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string IdWeb { get; init; } = string.Empty;
    public bool SuperAdmin { get; init; }
}

public sealed class BaseCentralDto
{
    public int IdBase { get; init; }
    public string IdCliente { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string DbServer { get; init; } = string.Empty;
    public string DbName { get; init; } = string.Empty;
    public string DbUser { get; init; } = string.Empty;
    public string DbPassword { get; init; } = string.Empty;
    public string? WebhookToken { get; init; }
}

public sealed class SesionCentralDto
{
    public UsuarioCentralDto Usuario { get; init; } = new();
    public ClienteCentralDto Cliente { get; init; } = new();
    public IReadOnlyList<BaseCentralDto> Bases { get; init; } = Array.Empty<BaseCentralDto>();
    public BaseCentralDto? BaseActiva { get; init; }
}

public sealed class ConexionClienteDto
{
    public int IdBase { get; init; }
    public string IdCliente { get; init; } = string.Empty;
    public string NombreBase { get; init; } = string.Empty;
    public string ConnectionString { get; init; } = string.Empty;
}
