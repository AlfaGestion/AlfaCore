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

    Task<CotizacionAlfaConfigDto> GetAlfaConfigAsync(CancellationToken ct = default);

    Task SaveAlfaConfigAsync(CotizacionAlfaConfigDto config, CancellationToken ct = default);

    Task<CotizacionAlfaResultDto> BuildAlfaLinesAsync(string? clienteCodigo, CotizacionAlfaSelectionRequest selection, CancellationToken ct = default);

    Task<bool> PermiteDescuentoPorLineaAsync(CancellationToken ct = default);

    Task SetPermiteDescuentoPorLineaAsync(bool permitido, CancellationToken ct = default);

    /// <summary>Historial de versiones de una cotización (más nueva primero), para el selector de
    /// versiones y la pestaña Historial.</summary>
    Task<IReadOnlyList<CotizacionVersionSummaryDto>> GetVersionesAsync(long idCotizacion, CancellationToken ct = default);

    /// <summary>Cada envío por email de cualquier versión de la cotización (más nuevo primero),
    /// para la pestaña Historial -- a quién, cuándo, con qué cuenta y si se detectó apertura.</summary>
    Task<IReadOnlyList<CotizacionEmailEnvioDto>> GetEmailEnviosAsync(long idCotizacion, CancellationToken ct = default);

    /// <summary>Registra la apertura de un email a partir del token del píxel de seguimiento
    /// (no falla nunca visiblemente: si el token no existe más, no hace nada). Ver el aviso de
    /// confiabilidad en CotizacionEmailEnvioDto.</summary>
    Task RegistrarAperturaEmailAsync(string trackingToken, CancellationToken ct = default);

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

    /// <summary>Envía la versión por email: primero intenta el email propio de quien envía
    /// (Usuarios → Email propio), después el correo general de la empresa (Configuración General →
    /// Email) y, si ninguno está configurado, la cuenta de rescate de AlfaGestión (limitada a 200
    /// envíos por mes por instalación). Si la versión seguía en BORRADOR, queda marcada ENVIADA.
    /// Devuelve true si terminó usando la cuenta de rescate (para que la UI lo avise). "mensaje" es
    /// el texto que escribe/edita quien envía (arranca precargado con el texto predeterminado de
    /// Configuración, pero se puede cambiar); si viene vacío, se usa un texto mínimo genérico.</summary>
    Task<bool> SendByEmailAsync(long idVersion, string destinatario, string? publicUrl = null, string? mensaje = null, CancellationToken ct = default);

    /// <summary>Texto predeterminado para acompañar el envío por email (Cotizaciones → Configuración).
    /// Se precarga, editable, en el diálogo de "Enviar por email".</summary>
    Task<string> GetEmailTextoPredeterminadoAsync(CancellationToken ct = default);

    Task SetEmailTextoPredeterminadoAsync(string texto, CancellationToken ct = default);

    /// <summary>Asistente de IA: redacta el texto corto de acompañamiento del email (no la
    /// propuesta completa). Se usa tanto para el texto predeterminado de Configuración como al
    /// momento de enviar. Delega en ICrmCotizacionService.</summary>
    Task<string> GenerateEmailMessageAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default);

    // El PDF y la vista pública en HTML (/cotizacion-publica/{idbase}/{token} y su variante
    // .../pdf en Program.cs) se generan con ICotizacionDocumentService (plantillas + Playwright),
    // no acá -- así el link que se comparte muestra siempre lo mismo que el PDF. La portada ahora
    // es parte de cada plantilla (IDocumentTemplateService), no de Cotizaciones.
}
