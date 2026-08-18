namespace AlfaCore.Components.Shared.AlfaDesign;

public sealed class AlfaActionMenuCoordinator
{
    public event Action<Guid>? Opened;

    public void NotifyOpened(Guid menuId)
        => Opened?.Invoke(menuId);
}
