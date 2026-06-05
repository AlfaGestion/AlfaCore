namespace AlfaCore.Models;

public sealed class Vb6AuthTicketRequest
{
    public string Servidor { get; init; } = string.Empty;
    public string BaseDatos { get; init; } = string.Empty;
    public string UsuarioSql { get; init; } = string.Empty;
    public string PasswordSql { get; init; } = string.Empty;
    public string UsuarioSistema { get; init; } = string.Empty;
    public string PasswordSistema { get; init; } = string.Empty;
    public string Modulo { get; init; } = string.Empty;
    public string? NombreSesion { get; init; }
}

public sealed class Vb6ConsumeTicketResult
{
    public string SqlSessionId { get; init; } = string.Empty;
    public string UserToken { get; init; } = string.Empty;
    public string RedirectUrl { get; init; } = string.Empty;
}

public sealed record Vb6BridgeTicketRecord
{
    public string Ticket { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public string Servidor { get; init; } = string.Empty;
    public string BaseDatos { get; init; } = string.Empty;
    public string UsuarioSql { get; init; } = string.Empty;
    public string PasswordSql { get; init; } = string.Empty;
    public string UsuarioSistema { get; init; } = string.Empty;
    public string PasswordSistema { get; init; } = string.Empty;
    public string Modulo { get; init; } = string.Empty;
    public string? NombreSesion { get; init; }
}
