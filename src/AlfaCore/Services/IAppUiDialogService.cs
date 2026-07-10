using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAppUiDialogService
{
    event Action? Changed;
    AppUiDialogState? Current { get; }

    Task ShowAsync(AppUiDialogState state);
    Task HideAsync();
    Task ShowBusyAsync(string title, string message, string? detail = null);
    Task ShowMessageAsync(AppUiFeedbackSeverity severity, string title, string message, string? detail = null, string? iconClass = null);
    Task ShowSuccessAsync(string title, string message, string? detail = null);
    Task<bool> ConfirmAsync(string title, string message, string primaryLabel, string secondaryLabel, string? detail = null, string? iconClass = null);
    Task<bool> ShowConfirmAsync(string title, string message, string primaryLabel, string secondaryLabel, string? detail = null, string? iconClass = null);
}
