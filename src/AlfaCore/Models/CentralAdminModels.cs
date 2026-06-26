namespace AlfaCore.Models;

public sealed class UsuarioCentralGridDto
{
    public string UserName { get; init; } = string.Empty;
    public string IdCliente { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
}

public sealed class AdminClienteDto
{
    public string IdCliente { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string IdWeb { get; init; } = string.Empty;
    public bool SuperAdmin { get; init; }
}

public sealed class AdminBaseDto
{
    public int IdBase { get; init; }
    public string IdCliente { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string DbServer { get; init; } = string.Empty;
    public string DbName { get; init; } = string.Empty;
    public string DbUser { get; init; } = string.Empty;
    public string DbPassword { get; init; } = string.Empty;
}

public sealed class AdminUserDto
{
    public string UserName { get; init; } = string.Empty;
    public string IdCliente { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed class ClienteAlfaLookupDto
{
    public string Codigo { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string Documento { get; init; } = string.Empty;
    public string Mail { get; init; } = string.Empty;
}

public sealed class CrearClienteRequest
{
    public string IdCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string IdWeb { get; set; } = string.Empty;
    public bool SuperAdmin { get; set; }
    public string? PasswordInicial { get; set; }
}

public sealed class CrearBaseRequest
{
    public int? IdBase { get; set; }
    public string IdCliente { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string DbServer { get; set; } = string.Empty;
    public string DbName { get; set; } = string.Empty;
    public string DbUser { get; set; } = string.Empty;
    public string DbPassword { get; set; } = string.Empty;
}

public sealed class CrearUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string IdCliente { get; set; } = string.Empty;
}
