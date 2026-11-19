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
    Lg,
    Xl
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

/// <param name="Group">Si se informa, las opciones consecutivas del mismo grupo se muestran juntas bajo ese título (optgroup).</param>
public sealed record AlfaSelectOption(string Value, string Label, bool Disabled = false, string? Group = null);

public sealed record AlfaTabItem(string Key, string Label, bool Disabled = false, string? Badge = null);
