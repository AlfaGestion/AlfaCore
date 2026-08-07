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
}
