namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

/// <summary>
/// Alias de cliente bancario almacenado en BK_AliasClienteBanco.
/// Permite identificar al cliente a partir del nombre en el extracto o su CUIT/CBU.
/// </summary>
public sealed class ClientAliasDto
{
    public string  ClientCode { get; init; } = string.Empty;
    public string  AliasText  { get; init; } = string.Empty;
    public string  ClientName { get; init; } = string.Empty;
    public string? Cuit       { get; init; }
    public string? ClientTaxId { get; init; }
    public string? CbuCvu     { get; init; }
}
