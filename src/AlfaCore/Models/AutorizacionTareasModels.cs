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
    public bool VerProforma { get; init; } = true;
    public string UNegocio { get; init; } = string.Empty;
    public string IdCaja { get; init; } = string.Empty;
    public string IdDeposito { get; init; } = string.Empty;
    public IReadOnlyList<AutorizacionCatalogoItemDto> UnidadesNegocio { get; init; } = [];
    public IReadOnlyList<AutorizacionCatalogoItemDto> Cajas { get; init; } = [];
    public IReadOnlyList<AutorizacionCatalogoItemDto> Depositos { get; init; } = [];
    public IReadOnlyList<MenuAutorizacionItemDto> Items { get; init; } = [];

    /// <summary>
    /// true = el usuario ya tiene opciones del menú cargadas (propias o de su perfil) y ve solo esas.
    /// false = no tiene ninguna: hoy ve todo el menú web.
    /// </summary>
    public bool Restringido { get; init; }

    /// <summary>Perfil asignado (null = sin perfil).</summary>
    public int? IdPerfil { get; init; }

    /// <summary>false si la base todavía no tiene las tablas de perfiles (no corrió la actualización).</summary>
    public bool PerfilesDisponibles { get; init; }

    public IReadOnlyList<PerfilWebResumenDto> Perfiles { get; init; } = [];
}

/// <summary>Perfil de permisos (Seguridad &gt; Perfiles): un grupo de opciones que se asigna a usuarios.</summary>
public sealed class PerfilWebResumenDto
{
    public int IdPerfil { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public IReadOnlyList<string> Claves { get; init; } = [];
    public IReadOnlyList<string> Usuarios { get; init; } = [];
}

public sealed class PerfilWebArbolDto
{
    public PerfilWebResumenDto? Perfil { get; init; }
    public IReadOnlyList<MenuAutorizacionItemDto> Items { get; init; } = [];
}

public sealed class GuardarPerfilWebRequest
{
    public int? IdPerfil { get; init; }
    public string MenuSistema { get; init; } = string.Empty;
    public string Sistema { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public IReadOnlyList<string> Claves { get; init; } = [];
}

public sealed class GuardarAutorizacionTareasRequest
{
    /// <summary>Perfil a asignar (null = sin perfil). Solo se usa si la base tiene perfiles.</summary>
    public int? IdPerfil { get; init; }

    public string MenuSistema { get; init; } = string.Empty;
    public string PermisosSistema { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public bool Administrador { get; init; }
    public bool VerProforma { get; init; } = true;
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

    /// <summary>Viene del perfil del usuario: se muestra tildado y no se puede destildar acá.</summary>
    public bool PorPerfil { get; set; }

    /// <summary>Permiso de acción (parte de un módulo, sin pantalla propia en el menú).</summary>
    public bool EsAccion { get; init; }

    /// <summary>Solo lo tiene quien lo tenga tildado: no se da por "sin permisos = ve todo".</summary>
    public bool Explicito { get; init; }

    public List<MenuAutorizacionItemDto> Hijos { get; init; } = [];

    public bool EsDesktop => !string.IsNullOrWhiteSpace(Proceso);

    public bool EsWeb => !string.IsNullOrWhiteSpace(RutaWeb) && HabilitadoWeb;

    public bool EsCompartida => EsDesktop && EsWeb;

    public bool EsNodoAgrupador => !EsDesktop && !EsWeb;
}
