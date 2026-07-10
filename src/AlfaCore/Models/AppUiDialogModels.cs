namespace AlfaCore.Models;

public enum AppUiDialogKind
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
    Loading = 4,
    Confirm = 5
}

public sealed class AppUiDialogButton
{
    public string Label { get; init; } = string.Empty;
    public string CssClass { get; init; } = "btn btn--ghost";
    public string? IconClass { get; init; }
    public bool IsPrimary { get; init; }
    public Func<Task>? Action { get; init; }
}

public sealed class AppUiDialogState
{
    public AppUiDialogKind Kind { get; init; } = AppUiDialogKind.Info;
    public AppUiFeedbackSeverity Severity { get; init; } = AppUiFeedbackSeverity.Info;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string IconClass { get; init; } = string.Empty;
    public bool ShowSpinner { get; init; }
    public bool CloseOnBackdropClick { get; init; }
    public TimeSpan? AutoCloseAfter { get; init; }
    public IReadOnlyList<AppUiDialogButton> Buttons { get; init; } = Array.Empty<AppUiDialogButton>();

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}
