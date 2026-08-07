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

/// <summary>
/// Borra por completo un cliente de prueba: logins centrales, base(s) registradas, pruebas de
/// módulos y el registro oficial (CUIT) en ALFANET2007 — para poder reusar el mismo CUIT/email en
/// un alta pública nueva. Solo pensado para datos de prueba; ver
/// <see cref="ICentralAdminService.ResetClientePruebaAsync"/> para el detalle de qué se borra y
/// qué se preserva si el cliente tiene actividad real.
/// </summary>
public sealed class ResetClientePruebaRequest
{
    public string IdCliente { get; set; } = string.Empty;
    /// <summary>Si es true, además borra la base de SQL Server física y su login — irreversible.</summary>
    public bool EliminarBaseFisica { get; set; }
}

public sealed class ResetClientePruebaResult
{
    public bool RegistroOficialEliminado { get; init; }
    public string? MotivoRegistroOficialNoEliminado { get; init; }
    public IReadOnlyList<string> BasesFisicasEliminadas { get; init; } = [];
}
