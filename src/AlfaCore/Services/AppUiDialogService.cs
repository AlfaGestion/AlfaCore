using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class AppUiDialogService : IAppUiDialogService
{
    private readonly object _sync = new();
    private AppUiDialogState? _current;
    private TaskCompletionSource<bool>? _pendingConfirm;

    public event Action? Changed;

    public AppUiDialogState? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public Task ShowAsync(AppUiDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        TaskCompletionSource<bool>? pendingConfirm = null;

        lock (_sync)
        {
            if (_current?.Kind == AppUiDialogKind.Confirm && _pendingConfirm is not null)
                pendingConfirm = _pendingConfirm;

            _pendingConfirm = state.Kind == AppUiDialogKind.Confirm ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            _current = state;
        }

        pendingConfirm?.TrySetResult(false);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public async Task HideAsync()
        => await HideAsyncInternal(null);

    private async Task HideAsyncInternal(bool? confirmResult)
    {
        TaskCompletionSource<bool>? pendingConfirm = null;
        lock (_sync)
        {
            if (_current?.Kind == AppUiDialogKind.Confirm && _pendingConfirm is not null)
                pendingConfirm = _pendingConfirm;

            _current = null;
            _pendingConfirm = null;
        }

        if (pendingConfirm is not null)
            pendingConfirm.TrySetResult(confirmResult ?? false);

        NotifyChanged();
        await Task.CompletedTask;
    }

    public Task ShowBusyAsync(string title, string message, string? detail = null)
        => ShowAsync(new AppUiDialogState
        {
            Kind = AppUiDialogKind.Loading,
            Severity = AppUiFeedbackSeverity.Info,
            Title = title,
            Message = message,
            Detail = detail ?? string.Empty,
            ShowSpinner = true,
            CloseOnBackdropClick = false
        });

    public Task ShowMessageAsync(AppUiFeedbackSeverity severity, string title, string message, string? detail = null, string? iconClass = null)
        => ShowAsync(new AppUiDialogState
        {
            Kind = AppUiDialogKind.Info,
            Severity = severity,
            Title = title,
            Message = message,
            Detail = detail ?? string.Empty,
            IconClass = iconClass ?? string.Empty,
            Buttons =
            [
                new AppUiDialogButton
                {
                    Label = "Entendido",
                    CssClass = "btn btn--primary",
                    IsPrimary = true,
                    Action = HideAsync
                }
            ],
            CloseOnBackdropClick = false
        });

    public Task ShowSuccessAsync(string title, string message, string? detail = null)
        => ShowAsync(new AppUiDialogState
        {
            Kind = AppUiDialogKind.Success,
            Severity = AppUiFeedbackSeverity.Success,
            Title = title,
            Message = message,
            Detail = detail ?? string.Empty,
            IconClass = "bi bi-check2-circle",
            Buttons = [],
            CloseOnBackdropClick = false,
            AutoCloseAfter = TimeSpan.FromSeconds(3)
        });

    public async Task<bool> ShowConfirmAsync(string title, string message, string primaryLabel, string secondaryLabel, string? detail = null, string? iconClass = null)
    {
        TaskCompletionSource<bool> pending;

        lock (_sync)
        {
            _pendingConfirm?.TrySetResult(false);
            pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingConfirm = pending;
            _current = new AppUiDialogState
            {
                Kind = AppUiDialogKind.Confirm,
                Severity = AppUiFeedbackSeverity.Warning,
                Title = title,
                Message = message,
                Detail = detail ?? string.Empty,
                IconClass = iconClass ?? "bi bi-exclamation-triangle",
                CloseOnBackdropClick = false,
                Buttons =
                [
                    new AppUiDialogButton
                    {
                        Label = secondaryLabel,
                        CssClass = "btn btn--ghost",
                        Action = async () =>
                        {
                            await HideAsyncInternal(false);
                            pending.TrySetResult(false);
                        }
                    },
                    new AppUiDialogButton
                    {
                        Label = primaryLabel,
                        CssClass = "btn btn--primary",
                        IsPrimary = true,
                        Action = async () =>
                        {
                            await HideAsyncInternal(true);
                            pending.TrySetResult(true);
                        }
                    }
                ]
            };
        }

        NotifyChanged();
        return await pending.Task;
    }

    private void NotifyChanged()
        => Changed?.Invoke();
}
