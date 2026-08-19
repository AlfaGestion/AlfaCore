namespace AlfaCore.Models;

public sealed class CatalogosClienteSessionInfo
{
    public string CodigoCliente { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string IdWeb { get; init; } = string.Empty;
    public int IdBase { get; init; }
    public DateTime LoginAt { get; init; } = DateTime.Now;
}

public sealed class CatalogosClienteLoginRequestDto
{
    public int? IdBase { get; set; }
    public string? IdWeb { get; set; }
    public int IdInsert { get; set; }
    public string CodigoCliente { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
