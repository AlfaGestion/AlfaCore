namespace AlfaCore.Models;

public sealed class UsuarioSistemaDto
{
    public string Nombre { get; init; } = string.Empty;
    public string Sistema { get; init; } = string.Empty;
    public bool Activo { get; init; }
    public bool Administrador { get; init; }
    public bool EsGrupo { get; init; }
}

public sealed class AutorizacionTareasDto
{
    public string MenuSistema { get; init; } = string.Empty;
    public string PermisosSistema { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public bool Administrador { get; init; }
    public string UNegocio { get; init; } = string.Empty;
    public string IdCaja { get; init; } = string.Empty;
    public string IdDeposito { get; init; } = string.Empty;
    public IReadOnlyList<AutorizacionCatalogoItemDto> UnidadesNegocio { get; init; } = [];
    public IReadOnlyList<AutorizacionCatalogoItemDto> Cajas { get; init; } = [];
    public IReadOnlyList<AutorizacionCatalogoItemDto> Depositos { get; init; } = [];
    public IReadOnlyList<MenuAutorizacionItemDto> Items { get; init; } = [];
}

public sealed class GuardarAutorizacionTareasRequest
{
    public string MenuSistema { get; init; } = string.Empty;
    public string PermisosSistema { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public bool Administrador { get; init; }
    public string UNegocio { get; init; } = string.Empty;
    public string IdCaja { get; init; } = string.Empty;
    public string IdDeposito { get; init; } = string.Empty;
    public IReadOnlyList<string> TareasSeleccionadas { get; init; } = [];
}

public sealed class AutorizacionCatalogoItemDto
{
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string UnidadNegocio { get; init; } = string.Empty;
    public bool Compartido { get; init; }

    public string CodigoVisual => Codigo.Trim();
    public string DescripcionVisual => Descripcion.Trim();
    public bool CodigoEsNumerico => int.TryParse(CodigoVisual, out _);
    public string Etiqueta => string.IsNullOrWhiteSpace(DescripcionVisual)
        ? CodigoVisual
        : $"{CodigoVisual} · {DescripcionVisual}";
}

public sealed class MenuAutorizacionToggleRequest
{
    public string Clave { get; init; } = string.Empty;
    public bool Checked { get; init; }
}

public sealed class MenuAutorizacionItemDto
{
    public string Menu { get; init; } = string.Empty;
    public string Clave { get; init; } = string.Empty;
    public string Titulo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string? Proceso { get; init; }
    public bool Habilitado { get; init; }
    public string? RutaWeb { get; init; }
    public bool HabilitadoWeb { get; init; }
    public bool Autorizado { get; set; }
    public bool Parcial { get; set; }
    public List<MenuAutorizacionItemDto> Hijos { get; init; } = [];

    public bool EsDesktop => !string.IsNullOrWhiteSpace(Proceso);

    public bool EsWeb => !string.IsNullOrWhiteSpace(RutaWeb) && HabilitadoWeb;

    public bool EsCompartida => EsDesktop && EsWeb;

    public bool EsNodoAgrupador => !EsDesktop && !EsWeb;
}
