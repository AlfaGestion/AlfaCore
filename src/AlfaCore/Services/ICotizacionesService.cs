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

    /// <summary>Historial de versiones de una cotización (más nueva primero), para el selector de
    /// versiones y la pestaña Historial.</summary>
    Task<IReadOnlyList<CotizacionVersionSummaryDto>> GetVersionesAsync(long idCotizacion, CancellationToken ct = default);

    /// <summary>Búsqueda de clientes (VT_CLIENTES) por código/razón social, para el selector de cliente.</summary>
    Task<IReadOnlyList<CotizacionClienteOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default);

    /// <summary>Resuelve nombre/lista/clase de un cliente ya elegido, reutilizando IArticuloPrecioResolverService.</summary>
    Task<CotizacionClienteInfoDto?> ResolveClienteAsync(string codigoCliente, CancellationToken ct = default);

    /// <summary>Métricas reales (montos y conteos) para la cabecera del listado.</summary>
    Task<CotizacionResumenDto> GetResumenAsync(CancellationToken ct = default);

    /// <summary>Asistente de IA: interpreta un pedido en lenguaje natural y busca los artículos
    /// reales del catálogo (precio siempre resuelto por IArticuloPrecioResolverService, la IA
    /// solo aporta el término de búsqueda y la cantidad). Delega en ICrmCotizacionService, que ya
    /// tiene esta lógica implementada y probada -- no se duplica.</summary>
    Task<IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>> SuggestLinesFromPromptAsync(string? clienteCodigo, string prompt, CancellationToken ct = default);

    /// <summary>Asistente de IA: redacta el texto de la propuesta (HTML simple) a partir de un
    /// pedido en lenguaje natural. Nunca inventa precios/importes. Delega en ICrmCotizacionService.</summary>
    Task<string> GenerateServiceProposalAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default);

    /// <summary>Envía la versión por email (misma cuenta SMTP configurada en TA_CONFIGURACION que
    /// ya usa el resto de la app -- EMAIL_SERVER/EMAIL_PORT/EMAIL_CTA/EMAIL_PASS/EMAIL_SSL). Si la
    /// versión seguía en BORRADOR, queda marcada ENVIADA.</summary>
    Task SendByEmailAsync(long idVersion, string destinatario, string? publicUrl = null, CancellationToken ct = default);

    /// <summary>PDF de la versión, para descarga desde el editor (sesión autenticada actual).</summary>
    Task<byte[]?> GeneratePdfAsync(long idVersion, CancellationToken ct = default);

    /// <summary>PDF de la versión resuelta por token público, mismo alcance que RenderPublicHtmlAsync
    /// (cruza a la base del cliente vía ICentralBasesService, sin depender de la sesión actual).</summary>
    Task<byte[]?> RenderPublicPdfAsync(int idBase, string token, CancellationToken ct = default);

    /// <summary>Imagen de portada usada como primera página del PDF (TA_LOGOS, IDLOGO='COT_PORTADA').</summary>
    Task<byte[]?> GetPortadaBytesAsync(CancellationToken ct = default);
    Task SavePortadaAsync(byte[] contenido, CancellationToken ct = default);
    Task DeletePortadaAsync(CancellationToken ct = default);
}
