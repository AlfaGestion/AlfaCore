namespace AlfaCore.Models;

/// <summary>
/// Nodo raíz de menú disponible para elegir como base de un módulo nuevo. Sale de
/// dbo.ALFACORE_MENU_WEB (misma estructura en todas las bases de clientes, confirmado
/// 2026-08-05 — ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md).
/// </summary>
public sealed class MenuRaizOptionDto
{
    public string Clave { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
}

/// <summary>
/// Una dependencia de módulo, ya resuelta con el código legible del módulo dependido.
/// Obligatoria = se activa gratis en cascada. Opcional = solo enganche informativo, que las
/// pantallas usan para decidir si mostrar una función puntual (ver
/// IsModuloActivoParaClienteActualAsync).
/// </summary>
public sealed class ModuloDependenciaDto
{
    public int IdModuloDependeDe { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public bool EsObligatoria { get; init; }
}

public sealed class ModuloDto
{
    public int Id { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string? Descripcion { get; init; }
    public string MenuKeyRaiz { get; init; } = string.Empty;
    public decimal Precio { get; init; }
    public bool Activo { get; init; }
    public List<ModuloDependenciaDto> Dependencias { get; init; } = [];
}

/// <summary>
/// Dependencia elegida al armar/editar un módulo desde /admin/modulos.
/// </summary>
public sealed class DependenciaSeleccionDto
{
    public int IdModulo { get; set; }
    public bool EsObligatoria { get; set; } = true;
}

public sealed class CrearModuloRequest
{
    public int? Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string MenuKeyRaiz { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public List<DependenciaSeleccionDto> Dependencias { get; set; } = [];
}

/// <summary>
/// Estado de un módulo del catálogo respecto de un cliente puntual — para la pantalla de
/// activación en Administrar. <see cref="EstaActivo"/> ya tiene en cuenta si el cliente es
/// legacy (todo activo sin filas propias en ClienteModulos).
/// </summary>
public sealed class ClienteModuloDto
{
    public int IdModulo { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public decimal Precio { get; init; }
    public bool EsDependenciaDeOtro { get; init; }
    public bool EstaActivo { get; init; }
    public string Estado { get; init; } = string.Empty;
    public DateTime? ActivadoUtc { get; init; }
    public string? ActivadoPor { get; init; }
    public DateTime? SolicitadoUtc { get; init; }
    public string? SolicitadoPor { get; init; }
    /// <summary>Solo tiene valor cuando <see cref="Estado"/> es <see cref="ClienteModuloEstados.Prueba"/>.</summary>
    public DateTime? PruebaVenceUtc { get; init; }
}

public static class ClienteModuloEstados
{
    public const string Solicitado = "Solicitado";
    public const string Activo = "Activo";
    public const string Prueba = "Prueba";
    public const string Suspendido = "Suspendido";
    public const string Rechazado = "Rechazado";
}

public sealed class ActivarModuloRequest
{
    public string IdCliente { get; set; } = string.Empty;
    public int IdModulo { get; set; }
    public string ActivadoPor { get; set; } = string.Empty;
}

/// <summary>
/// Duración de la prueba gratuita autoservicio que se ofrece al confirmar el registro público.
/// </summary>
public static class PruebaModuloDefaults
{
    public const int DiasDuracion = 30;
    public const int DiasAvisoPrevio = 5;
}

public sealed class IniciarPruebaModulosRequest
{
    public string IdCliente { get; set; } = string.Empty;
    public List<int> IdsModulos { get; set; } = [];
}

/// <summary>
/// Fila de prueba próxima a vencer (o ya vencida) — para el job de avisos/expiración.
/// </summary>
public sealed class PruebaModuloRecordatorioDto
{
    public string IdCliente { get; init; } = string.Empty;
    public string ClienteNombre { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public int IdModulo { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public decimal Precio { get; init; }
    public DateTime PruebaVenceUtc { get; init; }
}

public sealed class SolicitarModuloRequest
{
    public string IdCliente { get; set; } = string.Empty;
    public int IdModulo { get; set; }
    public string SolicitadoPor { get; set; } = string.Empty;
}

public sealed class RechazarModuloRequest
{
    public string IdCliente { get; set; } = string.Empty;
    public int IdModulo { get; set; }
    public string DecididoPor { get; set; } = string.Empty;
}

/// <summary>
/// Fila de la cola de aprobación en <c>/admin/solicitudes</c> — un módulo pedido por un cliente,
/// todavía no aprobado ni rechazado.
/// </summary>
public sealed class SolicitudModuloDto
{
    public string IdCliente { get; init; } = string.Empty;
    public string ClienteNombre { get; init; } = string.Empty;
    public int IdModulo { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public decimal Precio { get; init; }
    public DateTime? SolicitadoUtc { get; init; }
    public string? SolicitadoPor { get; init; }
}

/// <summary>
/// Filtro de menú por módulos para el cliente actual — ver
/// <see cref="ICentralAdminService.GetModuloMenuFiltroParaClienteActualAsync"/>.
/// </summary>
public sealed class ModuloMenuFiltroDto
{
    /// <summary>
    /// Claves que se muestran siempre, tenga o no módulo asociado — funciones de base del
    /// sistema que todo cliente necesita (gestión de usuarios propios, autorización de tareas,
    /// aplicar actualizaciones), no algo que se "contrata" por módulo. Compartido entre
    /// <c>MenuService</c> (arma el menú) y <c>AutorizacionTareasService</c> (arma el árbol de
    /// autorización) para que sean consistentes entre sí.
    /// </summary>
    public static readonly HashSet<string> NucleoSiempreVisible = new(StringComparer.OrdinalIgnoreCase)
    {
        "D015001", // Usuarios
        "D015002", // Autorización de tareas
        "D989003", // Actualizaciones
    };

    /// <summary>Todas las claves de menú (MenuKeyRaiz) que tienen un módulo definido en el catálogo.</summary>
    public HashSet<string> MenuKeysDefinidos { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>De las anteriores, las que corresponden a un módulo activo para este cliente.</summary>
    public HashSet<string> MenuKeysActivos { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Un nodo está permitido si él mismo o algún ancestro corresponde a un módulo activo para
    /// el cliente, o si está en <see cref="NucleoSiempreVisible"/>. Si ni él ni ningún ancestro
    /// tienen módulo definido en el catálogo, NO está permitido — a diferencia de
    /// <c>IsModuloActivoParaClienteActualAsync</c> (fail-open), acá solo se permite lo que el
    /// cliente efectivamente contrató.
    /// </summary>
    public bool PermiteClave(string clave, IReadOnlyDictionary<string, string> parentByKey)
    {
        var current = clave;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (NucleoSiempreVisible.Contains(current))
                return true;

            if (MenuKeysDefinidos.Contains(current))
                return MenuKeysActivos.Contains(current);

            current = parentByKey.TryGetValue(current, out var parent) ? parent : string.Empty;
        }

        return false;
    }
}
