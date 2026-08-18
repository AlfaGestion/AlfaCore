using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICatalogosClienteSessionService
{
    event Action? StateChanged;

    bool IsAuthenticated { get; }
    CatalogosClienteSessionInfo? CurrentClient { get; }
    string? CurrentToken { get; }

    Task<CatalogosClienteSessionInfo> LoginAsync(CatalogosClienteLoginRequestDto request, CancellationToken ct = default);
    bool TryRestoreFromToken(string token);
    void Logout();
}
