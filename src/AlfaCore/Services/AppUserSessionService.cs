using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class AppUserSessionService(
    ICentralAuthService centralAuthService,
    AppUserSessionStore sessionStore,
    IAppAuditActorAccessor auditActor) : IAppUserSessionService
{
    private AppUserSessionInfo? _currentUser;
    private string? _sessionToken;

    public event Action? StateChanged;

    public bool IsAuthenticated => _currentUser is not null;
    public bool RequiresInternalLogin => _currentUser?.RequiresInternalLogin == true;
    public AppUserSessionInfo? CurrentUser => _currentUser;
    public string? CurrentToken => _sessionToken;

    public async Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var result = await centralAuthService.LoginAsync(new LoginRequest
        {
            UserName = userName,
            Password = password
        }, ct);

        if (!result.Success || result.Session is null)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Message) ? "Usuario o contraseña incorrectos." : result.Message);

        _currentUser = new AppUserSessionInfo
        {
            UserName = result.Session.Usuario.UserName,
            Email = result.Session.Usuario.UserName,
            CentralLogin = userName.Trim(),
            SystemCode = "CENTRAL",
            LoginAt = DateTime.Now,
            IdCliente = result.Session.Cliente.IdCliente,
            RazonSocial = result.Session.Cliente.RazonSocial,
            IdWeb = result.Session.Cliente.IdWeb,
            SuperAdmin = result.Session.Cliente.SuperAdmin,
            RequiresInternalLogin = true
        };

        _sessionToken = sessionStore.Store(_currentUser);
        SyncAuditActor();
        StateChanged?.Invoke();
        return _currentUser;
    }

    public void AdoptInternalUser(AppUserSessionInfo internalUser)
    {
        ArgumentNullException.ThrowIfNull(internalUser);

        var baseInfo = _currentUser;
        _currentUser = new AppUserSessionInfo
        {
            UserName = internalUser.UserName,
            Email = internalUser.Email,
            CentralLogin = baseInfo?.CentralLogin ?? internalUser.CentralLogin,
            SystemCode = internalUser.SystemCode,
            LoginAt = baseInfo?.LoginAt ?? internalUser.LoginAt,
            SqlSessionId = baseInfo?.SqlSessionId ?? internalUser.SqlSessionId,
            SqlSessionName = baseInfo?.SqlSessionName ?? internalUser.SqlSessionName,
            IdCliente = baseInfo?.IdCliente ?? internalUser.IdCliente,
            RazonSocial = baseInfo?.RazonSocial ?? internalUser.RazonSocial,
            IdWeb = baseInfo?.IdWeb ?? internalUser.IdWeb,
            SuperAdmin = baseInfo?.SuperAdmin ?? internalUser.SuperAdmin,
            RequiresInternalLogin = false,
            AuthorizedSessionId = internalUser.AuthorizedSessionId
        };

        if (_sessionToken is not null)
            sessionStore.Upsert(_sessionToken, _currentUser);
        else
            _sessionToken = sessionStore.Store(_currentUser);

        SyncAuditActor();
        StateChanged?.Invoke();
    }

    public bool IsAuthorizedForSession(Guid? activeSessionId)
        => _currentUser is { RequiresInternalLogin: false }
           && activeSessionId is not null
           && _currentUser.AuthorizedSessionId == activeSessionId;

    public void EnsureAuthorizedForSession(Guid? activeSessionId)
    {
        if (_currentUser is null || _currentUser.RequiresInternalLogin)
            return;

        if (activeSessionId is not null && _currentUser.AuthorizedSessionId == activeSessionId)
            return;

        _currentUser = new AppUserSessionInfo
        {
            UserName = _currentUser.UserName,
            Email = _currentUser.Email,
            CentralLogin = _currentUser.CentralLogin,
            SystemCode = _currentUser.SystemCode,
            LoginAt = _currentUser.LoginAt,
            SqlSessionId = _currentUser.SqlSessionId,
            SqlSessionName = _currentUser.SqlSessionName,
            IdCliente = _currentUser.IdCliente,
            RazonSocial = _currentUser.RazonSocial,
            IdWeb = _currentUser.IdWeb,
            SuperAdmin = _currentUser.SuperAdmin,
            RequiresInternalLogin = true,
            AuthorizedSessionId = null
        };

        if (_sessionToken is not null)
            sessionStore.Upsert(_sessionToken, _currentUser);

        StateChanged?.Invoke();
    }

    public bool TryRestoreFromToken(string token)
    {
        if (!sessionStore.TryGet(token, out var info) || info is null)
            return false;

        _sessionToken = token;
        _currentUser = info;
        SyncAuditActor();
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

        _currentUser = null;
        auditActor.Clear();
        StateChanged?.Invoke();
    }

    public void HandleSqlSessionChanged()
    {
        if (_currentUser is null)
            return;

        var syncedUser = new AppUserSessionInfo
        {
            UserName = _currentUser.UserName,
            Email = _currentUser.Email,
            CentralLogin = _currentUser.CentralLogin,
            SystemCode = _currentUser.SystemCode,
            LoginAt = _currentUser.LoginAt,
            SqlSessionId = _currentUser.SqlSessionId,
            SqlSessionName = _currentUser.SqlSessionName,
            IdCliente = _currentUser.IdCliente,
            RazonSocial = _currentUser.RazonSocial,
            IdWeb = _currentUser.IdWeb,
            SuperAdmin = _currentUser.SuperAdmin,
            RequiresInternalLogin = _currentUser.RequiresInternalLogin,
            AuthorizedSessionId = _currentUser.AuthorizedSessionId
        };

        if (IsSameUser(_currentUser, syncedUser))
            return;

        _currentUser = syncedUser;

        if (_sessionToken is not null)
            sessionStore.Upsert(_sessionToken, _currentUser);

        SyncAuditActor();
        StateChanged?.Invoke();
    }

    private static bool IsSameUser(AppUserSessionInfo left, AppUserSessionInfo right)
        => string.Equals(left.UserName, right.UserName, StringComparison.Ordinal)
           && string.Equals(left.Email, right.Email, StringComparison.Ordinal)
           && string.Equals(left.CentralLogin, right.CentralLogin, StringComparison.Ordinal)
           && string.Equals(left.SystemCode, right.SystemCode, StringComparison.Ordinal)
           && left.LoginAt == right.LoginAt
           && left.SqlSessionId == right.SqlSessionId
           && string.Equals(left.SqlSessionName, right.SqlSessionName, StringComparison.Ordinal)
           && string.Equals(left.IdCliente, right.IdCliente, StringComparison.Ordinal)
           && string.Equals(left.RazonSocial, right.RazonSocial, StringComparison.Ordinal)
           && string.Equals(left.IdWeb, right.IdWeb, StringComparison.Ordinal)
           && left.SuperAdmin == right.SuperAdmin
           && left.RequiresInternalLogin == right.RequiresInternalLogin
           && left.AuthorizedSessionId == right.AuthorizedSessionId;

    public string GetCurrentUserName(string fallback = "")
        => _currentUser?.UserName ?? fallback;

    private void SyncAuditActor()
    {
        auditActor.UserName = _currentUser?.UserName?.Trim() ?? string.Empty;
        auditActor.CentralLogin = _currentUser?.CentralLogin?.Trim() ?? string.Empty;
    }
}
