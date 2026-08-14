namespace AlfaCore.Components.Shared.AlfaDesign;

public sealed class AlfaActionMenuItem
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string SecondaryText { get; init; } = string.Empty;
    public bool Active { get; init; }
    public bool Disabled { get; init; }
    public bool Danger { get; init; }
    public bool SeparatorBefore { get; init; }
    public Func<Task>? OnClick { get; init; }
}

public enum AlfaActionMenuAlign
{
    Start,
    End
}
