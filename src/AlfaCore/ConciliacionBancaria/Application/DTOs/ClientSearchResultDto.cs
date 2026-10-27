namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

/// <summary>
/// Resultado de búsqueda de cliente en VT_CLIENTES / MA_CUENTAS.
/// Se usa como fallback cuando el nombre del movimiento no coincide
/// con ningún alias registrado en BK_AliasClienteBanco.
/// </summary>
public sealed class ClientSearchResultDto
{
    public string  ClientCode  { get; init; } = string.Empty;
    public string  ClientName  { get; init; } = string.Empty;
    public string? Cuit        { get; init; }

    /// <summary>Puntuación de similitud de nombre (0-100, mayor = mejor).</summary>
    public int     MatchScore  { get; init; }
}
