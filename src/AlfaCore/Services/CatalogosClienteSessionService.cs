using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class CatalogosClienteSessionService(
    IInterfacesCatalogosService catalogosService,
    CatalogosClienteSessionStore sessionStore) : ICatalogosClienteSessionService
{
    private CatalogosClienteSessionInfo? _currentClient;
    private string? _sessionToken;

    public event Action? StateChanged;

    public bool IsAuthenticated => _currentClient is not null;
    public CatalogosClienteSessionInfo? CurrentClient => _currentClient;
    public string? CurrentToken => _sessionToken;

    public async Task<CatalogosClienteSessionInfo> LoginAsync(CatalogosClienteLoginRequestDto request, CancellationToken ct = default)
    {
        var client = await catalogosService.LoginClienteAsync(request, ct);
        _currentClient = client;

        if (_sessionToken is not null)
            sessionStore.Upsert(_sessionToken, client);
        else
            _sessionToken = sessionStore.Store(client);

        StateChanged?.Invoke();
        return client;
    }

    public bool TryRestoreFromToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (!sessionStore.TryGet(token, out var client) || client is null)
            return false;

        _sessionToken = token;
        _currentClient = client;
        StateChanged?.Invoke();
        return true;
    }

    public void Logout()
    {
        if (_sessionToken is not null)
        {
            sessionStore.Remove(_sessionToken);
            _sessionToken = null;
        }

        _currentClient = null;
        StateChanged?.Invoke();
    }
}
