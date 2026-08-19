using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Motor de facturación de suscripciones: genera cargos, registra pagos manuales y procesa
/// vencimientos/gracia/suspensión. Ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, Fase 3.
/// No reemplaza <see cref="ICentralAdminService"/> — lo complementa; para suspender un
/// <c>ClienteModulos</c> por mora real (fuera de gracia) hace el UPDATE directo dentro de su propia
/// transacción en vez de llamar a <see cref="ICentralAdminService.SuspenderModuloAsync"/> (que abre
/// su propia conexión) para mantener atómica la actualización del Cargo + ClienteModulos.
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Crea un Cargo <see cref="CargoEstados.Pendiente"/> para un <c>ClienteModulos</c>, usando
    /// Concepto/Importe/Moneda del plan contratado (snapshot <c>PrecioContratado</c>/
    /// <c>MonedaContratada</c>, con fallback al precio actual del plan si no hay snapshot cargado).
    /// Idempotente por <c>(IdClienteModulo, PeriodoDesde)</c>: si ya existe un cargo para ese
    /// período, devuelve el existente en vez de duplicar o fallar.
    /// </summary>
    Task<CargoDto?> GenerarCargoAsync(int idClienteModulo, DateTime periodoDesde, DateTime periodoHasta, CancellationToken ct = default);

    /// <summary>
    /// Registra un pago ya confirmado (hoy siempre vía <see cref="ManualPaymentProvider"/>): crea el
    /// Pago <see cref="PagoEstados.Aprobado"/>, marca el Cargo <see cref="CargoEstados.Pagado"/>,
    /// avanza <c>ClienteModulos.FechaProximoCobro</c> al siguiente período según el
    /// <c>TipoFacturacion</c> del plan contratado, y si el módulo estaba
    /// <c>Suspendido</c> por mora lo vuelve a <c>Activo</c>. Todo en una transacción Dapper.
    /// </summary>
    Task RegistrarPagoManualAsync(RegistrarPagoManualRequest request, string registradoPor, CancellationToken ct = default);

    /// <summary>
    /// Job diario: recorre <c>ClienteModulos</c> en estado <c>Activo</c> (no legacy, no prueba) con
    /// <c>FechaProximoCobro</c> ya cumplida y sin un Cargo <c>PENDIENTE</c>/<c>VENCIDO</c> abierto
    /// para ese período — genera el cargo correspondiente.
    /// </summary>
    Task ProcesarVencimientosAsync(CancellationToken ct = default);

    /// <summary>
    /// Job diario: recorre Cargos <c>PENDIENTE</c> donde <c>FechaVencimiento + Plan.DiasGracia</c>
    /// ya pasó — los marca <c>VENCIDO</c> y suspende el <c>ClienteModulos</c> asociado, con auditoría.
    /// </summary>
    Task ProcesarGraciaYSuspensionAsync(CancellationToken ct = default);

    /// <summary>
    /// Listado paginado de Cargos (server-side, regla 27 de CODEX_RULES.md) para
    /// <c>/admin/cargos</c>. Enriquece cada fila con <see cref="CargoDto.ClienteNombre"/>.
    /// </summary>
    Task<PagedResult<CargoDto>> SearchCargosAsync(CargosFilters filters, CancellationToken ct = default);

    /// <summary>
    /// Listado paginado de Pagos (server-side, regla 27 de CODEX_RULES.md) para
    /// <c>/admin/pagos</c>. Enriquece cada fila con <see cref="PagoDto.ClienteNombre"/>.
    /// </summary>
    Task<PagedResult<PagoDto>> SearchPagosAsync(PagosFilters filters, CancellationToken ct = default);

    /// <summary>
    /// Configuración de vista del listado de Cargos, por usuario — regla 26 de CODEX_RULES.md.
    /// Se persiste en <c>dbo.TA_CONFIGURACION</c> de la base activa de la sesión (mismo patrón
    /// que <c>ContactosService</c>/<c>UsuariosService</c>), no en ALFA_CENTRAL.
    /// </summary>
    Task<CargosViewSettingsDto> GetCargosViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveCargosViewSettingsAsync(string userName, CargosViewSettingsDto settings, CancellationToken ct = default);

    /// <summary>
    /// Configuración de vista del listado de Pagos, por usuario — regla 26 de CODEX_RULES.md.
    /// Mismo mecanismo de persistencia que <see cref="GetCargosViewSettingsAsync"/>.
    /// </summary>
    Task<PagosViewSettingsDto> GetPagosViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SavePagosViewSettingsAsync(string userName, PagosViewSettingsDto settings, CancellationToken ct = default);
}
