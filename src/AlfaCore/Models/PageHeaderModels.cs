namespace AlfaCore.Models;

using Microsoft.AspNetCore.Components;

public enum PageHeaderState
{
    Idle,
    List,
    Kanban,
    Detail,
    Loading,
    Editing,
    Creating,
    ReadOnly,
    Selection,
    SelectionMode
}

public enum PageHeaderActionStyle
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

public enum PageHeaderActionPriority
{
    Essential,
    Normal,
    Overflow
}

public enum PageHeaderShellMode
{
    Legacy,
    AlfaDesignPilot
}

public sealed class PageHeaderConfig
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<string> Breadcrumb { get; init; } = [];
    public PageHeaderState State { get; init; } = PageHeaderState.Idle;
    public PageHeaderShellMode ShellMode { get; init; } = PageHeaderShellMode.Legacy;

    /// <summary>Módulo dueño de este header (primer segmento de ruta, ej. "articulos", "clientes").
    /// Opcional -- si una página no lo completa, MainLayout usa el criterio histórico (solo ShellMode +
    /// ruta gestionada) para decidir si preservar el header entre navegaciones. Si SÍ lo completa,
    /// MainLayout exige que coincida con el módulo actual antes de preservarlo, evitando que el header
    /// de una página AlfaDesignPilot quede pegado al navegar a OTRO módulo AlfaDesignPilot distinto
    /// (bug real: Clientes -> Artículos dejaba las acciones de Guardar/Cancelar apuntando a la
    /// instancia ya dispuesta de Clientes).</summary>
    public string ModuleKey { get; init; } = string.Empty;
    public IReadOnlyList<PageHeaderTopNavItem> TopNavigationItems { get; init; } = [];
    public PageHeaderSearchConfig? Search { get; init; }
    public RenderFragment? SearchContent { get; init; }
    public IReadOnlyList<PageHeaderAction> Actions { get; init; } = [];
    public IReadOnlyList<PageHeaderViewButton> ViewButtons { get; init; } = [];
    public PageHeaderPaginationConfig? Pagination { get; init; }
    public bool InlineViewButtons { get; init; }
    public bool ActionsBelowViewButtons { get; init; }
    public bool RecordToolbar { get; init; }
    public bool CompactContextToolbar { get; init; }
    public bool AlwaysVisible { get; init; }
    public bool ShowHistoryNavigation { get; init; } = true;
    public bool ShowHomeNavigation { get; init; }
    public string HomeUrl { get; init; } = "/";
    public string HomeTitle { get; init; } = "Ir a Inicio";
    public string HomeLogoSrc { get; init; } = "/logos/Logo.png";
    public string HomeIcon { get; init; } = "bi-house-door-fill";
    public Func<Task>? OnBack { get; init; }
    public Func<Task>? OnForward { get; init; }

    public static PageHeaderConfig Empty { get; } = new();
}

public sealed class PageHeaderTopNavItem
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public bool Active { get; init; }
    public bool Disabled { get; init; }
    public Func<Task>? OnClick { get; init; }
}

public sealed class PageHeaderSearchConfig
{
    public string Placeholder { get; init; } = "Buscar...";
    public string Text { get; init; } = string.Empty;
    public bool Disabled { get; init; }
    public bool ShowFiltersToggle { get; init; }
    public bool FiltersOpen { get; init; }
    public Func<string, Task>? OnTextChanged { get; init; }
    public Func<Task>? OnSearch { get; init; }
    public Func<Task>? OnToggleFilters { get; init; }
}

public sealed class PageHeaderPaginationConfig
{
    public string Label { get; init; } = string.Empty;
    public string PageLabel { get; init; } = string.Empty;
    public bool HasPrevious { get; init; }
    public bool HasNext { get; init; }
    public bool Disabled { get; init; }
    public Func<Task>? OnPrevious { get; init; }
    public Func<Task>? OnNext { get; init; }
}

public sealed class PageHeaderAction
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool Disabled { get; init; }
    public bool Busy { get; init; }
    public PageHeaderActionStyle Style { get; init; } = PageHeaderActionStyle.Secondary;
    public PageHeaderActionPriority Priority { get; init; } = PageHeaderActionPriority.Normal;
    public Func<Task>? OnClick { get; init; }
}

public sealed class PageHeaderViewButton
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public bool Active { get; init; }
    public bool Disabled { get; init; }
    public Func<Task>? OnClick { get; init; }
}
