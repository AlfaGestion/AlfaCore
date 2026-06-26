namespace AlfaCore.Models;

public sealed class AppUserSessionInfo
{
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string CentralLogin { get; init; } = string.Empty;
    public string SystemCode { get; init; } = string.Empty;
    public DateTime LoginAt { get; init; } = DateTime.Now;
    public Guid SqlSessionId { get; init; }
    public string SqlSessionName { get; init; } = string.Empty;
    public string IdCliente { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string IdWeb { get; init; } = string.Empty;
    public bool SuperAdmin { get; init; }
    public bool RequiresInternalLogin { get; init; }
}
