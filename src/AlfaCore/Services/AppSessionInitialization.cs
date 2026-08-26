namespace AlfaCore.Services;

public enum AppSessionInitializationState
{
    NotStarted,
    Restoring,
    ReadyAuthenticated,
    ReadyUnauthenticated
}

public interface IAppSessionInitialization
{
    AppSessionInitializationState State { get; }
    void BeginRestoring();
    void Complete(bool authenticated);
    Task<AppSessionInitializationState> WaitUntilReadyAsync(CancellationToken ct = default);
}

public sealed class AppSessionInitialization : IAppSessionInitialization
{
    private readonly TaskCompletionSource<AppSessionInitializationState> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AppSessionInitializationState _state = AppSessionInitializationState.NotStarted;

    public AppSessionInitializationState State => _state;

    public void BeginRestoring()
    {
        if (_state == AppSessionInitializationState.NotStarted)
            _state = AppSessionInitializationState.Restoring;
    }

    public void Complete(bool authenticated)
    {
        if (IsReady(_state))
            return;

        _state = authenticated
            ? AppSessionInitializationState.ReadyAuthenticated
            : AppSessionInitializationState.ReadyUnauthenticated;
        _ready.TrySetResult(_state);
    }

    public Task<AppSessionInitializationState> WaitUntilReadyAsync(CancellationToken ct = default)
        => IsReady(_state) ? Task.FromResult(_state) : _ready.Task.WaitAsync(ct);

    private static bool IsReady(AppSessionInitializationState state)
        => state is AppSessionInitializationState.ReadyAuthenticated
            or AppSessionInitializationState.ReadyUnauthenticated;
}
