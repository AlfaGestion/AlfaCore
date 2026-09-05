using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Módulo general de cotizaciones (no exclusivo de CRM): artículos, servicios/tareas, líneas
/// libres/informativas, versionado real (snapshot inmutable por versión) y el configurador
/// especial "Alfa Gestión". Ver CRM_COTIZACION/CrmCotizacionService para el cotizador previo
/// (artículos, sin versionado), que sigue funcionando en paralelo.
/// </summary>
public interface ICotizacionesService
{
    Task<PagedResult<CotizacionListItemDto>> GetListAsync(CotizacionListFiltersDto filters, CancellationToken ct = default);

    Task<IReadOnlyList<CotizacionListItemDto>> GetByOportunidadAsync(long idOportunidad, CancellationToken ct = default);

    Task<CotizacionVersionDetailDto?> GetVersionDetailAsync(long idVersion, CancellationToken ct = default);

    /// <summary>Crea el documento raíz + versión 1 (BORRADOR), en una transacción. Devuelve el IdVersion.</summary>
    Task<long> CreateAsync(CotizacionCreateRequest request, CancellationToken ct = default);

    /// <summary>Copia secciones/líneas/texto de la versión activa a una versión nueva. Devuelve el IdVersion nuevo.</summary>
    Task<long> CreateNewVersionAsync(long idCotizacion, string? usuarioAccion, CancellationToken ct = default);

    /// <summary>Reemplaza datos comerciales/secciones/líneas de una versión. Solo si sigue en BORRADOR.</summary>
    Task SaveVersionAsync(CotizacionSaveVersionRequest request, CancellationToken ct = default);

    Task MarkEnviadaAsync(long idVersion, string? usuarioAccion, CancellationToken ct = default);

    /// <summary>Marca la versión ACEPTADA y, si la cotización viene de una Oportunidad, intenta
    /// cerrarla como ganada reutilizando ICrmService.QuickUpdateAsync (nunca un UPDATE aislado).
    /// Si no hay una única etapa con EsGanada=1, no adivina: deja la cotización aceptada igual.</summary>
    Task<bool> MarkAceptadaAsync(long idVersion, string? usuarioAccion, CancellationToken ct = default);

    Task MarkRechazadaAsync(long idVersion, string? usuarioAccion, CancellationToken ct = default);

    Task AnularAsync(long idCotizacion, string? usuarioAccion, CancellationToken ct = default);

    Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(string? clienteCodigo, string texto, int take = 25, CancellationToken ct = default);

    Task<IReadOnlyList<CotizacionTareaDto>> SearchTareasAsync(string texto, int take = 25, CancellationToken ct = default);

    Task<CotizacionShareDto> EnsureShareAsync(long idVersion, CancellationToken ct = default);

    Task<string?> RenderPublicHtmlAsync(int idBase, string token, CancellationToken ct = default);

    Task<CotizacionAlfaConfigDto> GetAlfaConfigAsync(CancellationToken ct = default);

    Task SaveAlfaConfigAsync(CotizacionAlfaConfigDto config, CancellationToken ct = default);

    Task<CotizacionAlfaResultDto> BuildAlfaLinesAsync(string? clienteCodigo, CotizacionAlfaSelectionRequest selection, CancellationToken ct = default);

    Task<bool> PermiteDescuentoPorLineaAsync(CancellationToken ct = default);

    Task SetPermiteDescuentoPorLineaAsync(bool permitido, CancellationToken ct = default);
}
