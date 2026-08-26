namespace AlfaCore.Services;

public enum TenantIdentityState
{
    Unauthenticated,
    CentralAuthenticatedPendingInternalIdentity,
    TenantIdentityReady
}

public interface ITenantIdentityReadiness
{
    TenantIdentityState State { get; }
    Task<TenantIdentityState> WaitForTenantIdentityReadyAsync(CancellationToken ct = default);
}

public sealed class TenantIdentityReadiness(
    IAppUserSessionService appUserSession,
    ISessionService sessionService) : ITenantIdentityReadiness
{
    public TenantIdentityState State => ResolveState(
        appUserSession.IsAuthenticated,
        appUserSession.RequiresInternalLogin,
        sessionService.GetActiveSession()?.Id,
        appUserSession.IsAuthorizedForSession(sessionService.GetActiveSession()?.Id));

    public async Task<TenantIdentityState> WaitForTenantIdentityReadyAsync(CancellationToken ct = default)
    {
        while (State == TenantIdentityState.CentralAuthenticatedPendingInternalIdentity)
        {
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void NotifyChanged() => changed.TrySetResult();

            appUserSession.StateChanged += NotifyChanged;
            sessionService.SessionChanged += NotifyChanged;
            try
            {
                if (State != TenantIdentityState.CentralAuthenticatedPendingInternalIdentity)
                    continue;

                await changed.Task.WaitAsync(ct);
            }
            finally
            {
                appUserSession.StateChanged -= NotifyChanged;
                sessionService.SessionChanged -= NotifyChanged;
            }
        }

        return State;
    }

    public static TenantIdentityState ResolveState(
        bool isAuthenticated,
        bool requiresInternalLogin,
        Guid? activeSessionId,
        bool isAuthorizedForActiveSession)
    {
        if (!isAuthenticated)
            return TenantIdentityState.Unauthenticated;

        return !requiresInternalLogin
               && activeSessionId is not null
               && isAuthorizedForActiveSession
            ? TenantIdentityState.TenantIdentityReady
            : TenantIdentityState.CentralAuthenticatedPendingInternalIdentity;
    }
}
