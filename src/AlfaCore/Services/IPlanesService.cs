using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// CRUD de planes de comercialización por módulo (<c>dbo.Planes</c>, en ALFA_CENTRAL). Ver
/// docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, Fase 3.
/// </summary>
public interface IPlanesService
{
    Task<PagedResult<PlanDto>> SearchAsync(PlanesFilters filters, CancellationToken ct = default);

    /// <summary>Todos los planes activos e inactivos de un módulo, para la pantalla de administración.</summary>
    Task<IReadOnlyList<PlanDto>> GetByModuloAsync(int idModulo, CancellationToken ct = default);

    Task<PlanDto?> GetByIdAsync(int id, CancellationToken ct = default);

    Task CreateAsync(CrearPlanRequest request, CancellationToken ct = default);

    Task UpdateAsync(CrearPlanRequest request, CancellationToken ct = default);

    /// <summary>Baja/alta lógica (no física) — un plan dado de baja deja de ofrecerse pero no se borra.</summary>
    Task SetActivoAsync(int id, bool activo, CancellationToken ct = default);

    /// <summary>
    /// Planes activos y visibles en catálogo (<c>Activo = 1 AND VisibleCatalogo = 1</c>, del
    /// módulo también activo), agrupados por <c>Modulos.Codigo</c> (case-insensitive) — para que
    /// las landing pages públicas (<c>LandingModulos.razor</c>/<c>LandingModulo.razor</c>) y el
    /// selector de <c>Verify.razor</c> muestren precios reales en vez del precio hardcodeado de
    /// <c>LandingContenidoCatalogo</c>. Un módulo sin ningún plan cargado todavía simplemente no
    /// aparece en el resultado (esas pantallas mantienen el comportamiento histórico para ese caso).
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<PlanDto>>> GetPlanesVisiblesPorCodigoModuloAsync(CancellationToken ct = default);
}
