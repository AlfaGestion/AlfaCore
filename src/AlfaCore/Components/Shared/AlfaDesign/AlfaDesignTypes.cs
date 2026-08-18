namespace AlfaCore.Components.Shared.AlfaDesign;

public enum AlfaButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

public enum AlfaSize
{
    Sm,
    Md
}

public enum AlfaDialogSize
{
    Sm,
    Md,
    Lg
}

public enum AlfaTagTone
{
    Neutral,
    Accent,
    Success,
    Warning,
    Danger
}

public enum AlfaEmptyStateKind
{
    NoData,
    NoResults,
    Error
}

public sealed record AlfaSelectOption(string Value, string Label, bool Disabled = false);

public sealed record AlfaTabItem(string Key, string Label, bool Disabled = false, string? Badge = null);
