namespace AlfaCore.Services;

public enum FloatingWindowType
{
    DropdownMenu,
    Popover,
    LookupResults,
    CommandPalette
}

public interface IFloatingWindowService
{
    IDisposable Register(FloatingWindowType type, Func<Task> closeAsync);
    Task CloseAllAsync();
}
