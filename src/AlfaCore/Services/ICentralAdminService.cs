using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralAdminService
{
    Task<IReadOnlyList<AdminClienteDto>> GetClientesAsync(CancellationToken ct = default);
    Task<AdminClienteDto?> GetClienteAsync(string idCliente, CancellationToken ct = default);
    Task<IReadOnlyList<ClienteAlfaLookupDto>> SearchVtClientesAsync(string term, int take = 25, CancellationToken ct = default);
    Task CreateClienteAsync(CrearClienteRequest request, CancellationToken ct = default);
    Task UpdateClienteAsync(CrearClienteRequest request, CancellationToken ct = default);
    Task<string?> TryResolveInitialPasswordAsync(string idCliente, CancellationToken ct = default);

    Task<IReadOnlyList<AdminBaseDto>> GetBasesAsync(CancellationToken ct = default);
    Task<AdminBaseDto?> GetBaseAsync(int idBase, CancellationToken ct = default);
    Task CreateBaseAsync(CrearBaseRequest request, CancellationToken ct = default);
    Task UpdateBaseAsync(CrearBaseRequest request, CancellationToken ct = default);
    Task DeleteBaseAsync(int idBase, CancellationToken ct = default);

    /// <summary>
    /// Borra por completo un cliente de prueba (módulos, logins, bases y el registro oficial con
    /// el CUIT) para poder reusar los mismos datos en un alta pública nueva. A diferencia de
    /// <see cref="DeleteBaseAsync"/>/<see cref="DeleteUserAsync"/>, que solo desvinculan una fila.
    /// </summary>
    Task<ResetClientePruebaResult> ResetClientePruebaAsync(ResetClientePruebaRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(CancellationToken ct = default);
    Task<AdminUserDto?> GetUserAsync(string userName, CancellationToken ct = default);
    Task CreateUserAsync(CrearUserRequest request, CancellationToken ct = default);
    Task UpdateUserAsync(CrearUserRequest request, CancellationToken ct = default);
    Task DeleteUserAsync(string userName, CancellationToken ct = default);

    /// <summary>
    /// Nodos raíz de <c>ALFACORE_MENU_WEB</c> disponibles para asignar como base de un módulo
    /// nuevo — se lee de la base por defecto porque el árbol de menú es igual en todas las
    /// bases de clientes (confirmado 2026-08-05).
    /// </summary>
    Task<IReadOnlyList<MenuRaizOptionDto>> GetMenuRaizOptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ModuloDto>> GetModulosAsync(CancellationToken ct = default);
    Task<ModuloDto?> GetModuloAsync(int idModulo, CancellationToken ct = default);
    Task CreateModuloAsync(CrearModuloRequest request, CancellationToken ct = default);
    Task UpdateModuloAsync(CrearModuloRequest request, CancellationToken ct = default);
    Task DeleteModuloAsync(int idModulo, CancellationToken ct = default);

    /// <summary>
    /// Estado del catálogo de módulos para un cliente puntual, para la pantalla de activación
    /// en Administrar.
    /// </summary>
    Task<IReadOnlyList<ClienteModuloDto>> GetClienteModulosAsync(string idCliente, CancellationToken ct = default);

    /// <summary>
    /// Activa un módulo para un cliente, y en cascada todos los módulos de los que depende que
    /// todavía no estén activos (las dependencias se activan sin cargo).
    /// </summary>
    Task ActivarModuloAsync(ActivarModuloRequest request, CancellationToken ct = default);

    Task SuspenderModuloAsync(string idCliente, int idModulo, CancellationToken ct = default);

    /// <summary>
    /// Contrata un Plan para un cliente: reusa el núcleo de activación en cascada (dependencias
    /// obligatorias gratis) para dejar el <c>ClienteModulos</c> del módulo dueño del plan en
    /// <c>Activo</c>, o en <c>Prueba</c> si el plan tiene <c>DiasPrueba</c> y el cliente todavía no
    /// usó una prueba para ese módulo. Setea <c>PrecioContratado</c>/<c>MonedaContratada</c> como
    /// snapshot del precio del plan al momento de contratar (no cambia si el plan sube de precio
    /// después) y calcula <c>FechaProximoCobro</c> según <c>TipoFacturacion</c>.
    /// </summary>
    Task ContratarPlanAsync(string idCliente, int idPlan, string contratadoPor, CancellationToken ct = default);

    /// <summary>
    /// Cambia el plan contratado de un <c>ClienteModulos</c> ya existente. Sin prorrateo (decisión
    /// de producto): solo actualiza <c>IdPlan</c>/<c>PrecioContratado</c>/<c>MonedaContratada</c>
    /// para que el PRÓXIMO cargo que genere <see cref="IBillingService"/> use el plan nuevo — no
    /// toca cargos ya emitidos ni el período en curso.
    /// </summary>
    Task CambiarPlanAsync(string idCliente, int idModulo, int nuevoIdPlan, string cambiadoPor, CancellationToken ct = default);

    /// <summary>
    /// True si el cliente ya tuvo alguna vez una prueba gratuita de este módulo (<c>PruebaVenceUtc</c>
    /// cargado en su fila de <c>ClienteModulos</c> alguna vez, sea cual sea el estado actual hoy) —
    /// mismo criterio que usa <see cref="ContratarPlanAsync"/> para decidir si una contratación
    /// nueva arranca en <c>Prueba</c> o directo en <c>Activo</c>. Expuesto públicamente para que el
    /// registro público (ver <c>CentralRegistrationService</c>) pueda decidir, ANTES de llamar a
    /// <see cref="ContratarPlanAsync"/>, si la elección de un plan puede autoservirse en Prueba o
    /// si tiene que quedar como solicitud pendiente de aprobación manual — el registro público
    /// nunca debe dejar un <c>ClienteModulos</c> en <c>Activo</c> directo sin pago confirmado.
    /// </summary>
    Task<bool> ClienteYaUsoPruebaModuloAsync(string idCliente, int idModulo, CancellationToken ct = default);

    /// <summary>
    /// Autoservicio: arranca la prueba gratuita de 30 días para los módulos elegidos al
    /// confirmar el registro público (ver <see cref="PruebaModuloDefaults"/>).
    /// </summary>
    Task IniciarPruebaModulosAsync(IniciarPruebaModulosRequest request, CancellationToken ct = default);

    /// <summary>
    /// Pruebas vigentes que vencen dentro de N días y no recibieron aviso en las últimas 24hs —
    /// para el job diario de recordatorios.
    /// </summary>
    Task<IReadOnlyList<PruebaModuloRecordatorioDto>> GetPruebasPorVencerAsync(int diasAntes, CancellationToken ct = default);

    Task MarcarRecordatorioPruebaEnviadoAsync(string idCliente, int idModulo, CancellationToken ct = default);

    /// <summary>
    /// Suspende toda prueba vencida sin conversión a pago. Devuelve la cantidad de filas afectadas.
    /// </summary>
    Task<int> ExpirarPruebasVencidasAsync(CancellationToken ct = default);

    /// <summary>
    /// Cola de solicitudes pendientes de aprobación, cruzando todos los clientes — para
    /// <c>/admin/solicitudes</c>.
    /// </summary>
    Task<IReadOnlyList<SolicitudModuloDto>> GetSolicitudesPendientesAsync(CancellationToken ct = default);

    /// <summary>
    /// Deja pedido un módulo para un cliente (estado Solicitado) sin activarlo todavía — para
    /// cuando el cliente lo pide pero todavía no se confirmó el pago.
    /// </summary>
    Task SolicitarModuloAsync(SolicitarModuloRequest request, CancellationToken ct = default);

    /// <summary>
    /// Rechaza una solicitud pendiente (queda marcada como Rechazada, no se borra).
    /// </summary>
    Task RechazarModuloAsync(RechazarModuloRequest request, CancellationToken ct = default);

    /// <summary>
    /// Para usar desde pantallas normales (no solo Administrar): si el módulo con ese código
    /// está definido y NO está activo para el cliente del usuario logueado, la pantalla que
    /// llama debería ocultar la función correspondiente. Devuelve <c>true</c> (fail-open) en
    /// modo legacy/on-premise, cliente legacy, módulo no definido todavía, o ante cualquier
    /// error — nunca oculta una función por un problema de infraestructura.
    /// </summary>
    Task<bool> IsModuloActivoParaClienteActualAsync(string codigoModulo, CancellationToken ct = default);

    /// <summary>
    /// Para usar desde <c>MenuService</c>: filtro del menú lateral según los módulos contratados
    /// por el cliente del usuario logueado. <c>null</c> = no filtrar (legacy/on-premise).
    /// </summary>
    Task<ModuloMenuFiltroDto?> GetModuloMenuFiltroParaClienteActualAsync(CancellationToken ct = default);

    /// <summary>
    /// Estado (activa/suspendida) de cada landing de <c>LandingContenidoCatalogo</c> — para la
    /// sección de administración de landings. Una landing sin fila en dbo.LandingModulos está
    /// activa por defecto.
    /// </summary>
    Task<IReadOnlyList<LandingEstadoDto>> GetLandingEstadosAsync(CancellationToken ct = default);

    /// <summary>
    /// Activa o suspende una landing pública (/landing/{slug}). Suspendida = la página deja de
    /// mostrarse y no aparece en /modulos, pero el contenido sigue viviendo en el catálogo hardcodeado.
    /// </summary>
    Task SetLandingActivoAsync(string slug, bool activo, CancellationToken ct = default);
}
