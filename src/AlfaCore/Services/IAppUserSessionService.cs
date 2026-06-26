using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAppUserSessionService
{
    event Action? StateChanged;

    bool IsAuthenticated { get; }
    AppUserSessionInfo? CurrentUser { get; }
    bool RequiresInternalLogin { get; }

    string? CurrentToken { get; }

    Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default);
    void AdoptInternalUser(AppUserSessionInfo internalUser);
    bool TryRestoreFromToken(string token);
    void Logout();
    void HandleSqlSessionChanged();
    string GetCurrentUserName(string fallback = "");
}
