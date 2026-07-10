namespace AlfaCore.Models;

public sealed class ShellMenuNodeDto
{
    public string Menu { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Proceso { get; set; } = string.Empty;
    public string RutaWeb { get; set; } = string.Empty;
    public string Componente { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public int OrdenWeb { get; set; }
    public bool EsFavoritoDefault { get; set; }
    public string Observacion { get; set; } = string.Empty;
    public DateTime? UltimoAcceso { get; set; }
    public bool TieneRutaWeb => !string.IsNullOrWhiteSpace(RutaWeb);
}

public sealed class ShellModuleDto
{
    public string Menu { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string RutaWeb { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public int OrdenWeb { get; set; }
}

public sealed class ShellWorkspaceSectionDto
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public IReadOnlyList<ShellMenuNodeDto> Items { get; set; } = [];
}

public sealed class ShellHomeDto
{
    public IReadOnlyList<ShellModuleDto> Modules { get; set; } = [];
    public IReadOnlyList<ShellMenuNodeDto> Favorites { get; set; } = [];
    public IReadOnlyList<ShellMenuNodeDto> Recents { get; set; } = [];
}

public sealed class ShellWorkspaceDto
{
    public ShellModuleDto? Module { get; set; }
    public IReadOnlyList<ShellWorkspaceSectionDto> Sections { get; set; } = [];
    public IReadOnlyList<ShellMenuNodeDto> Favorites { get; set; } = [];
    public IReadOnlyList<ShellMenuNodeDto> Recents { get; set; } = [];
}

public sealed class ShellRouteContextDto
{
    public ShellModuleDto? Module { get; set; }
    public ShellMenuNodeDto? ActiveNode { get; set; }
    public IReadOnlyList<ShellModuleDto> Modules { get; set; } = [];
}

public sealed class ShellMenuSearchItemDto
{
    public string Menu { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string RutaWeb { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public string ModuloNombre { get; set; } = string.Empty;
    public string SeccionNombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    public bool EsModulo { get; set; }
}
