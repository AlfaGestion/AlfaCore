using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaConfigService
{
    Task<IReadOnlyList<PuntoVentaEntidadDto>> GetPuntosVentaAsync(bool soloActivos = false, CancellationToken ct = default);
    Task<PuntoVentaCatalogosDto> GetCatalogosAsync(CancellationToken ct = default);
    Task<string> GetComprobanteHabitualAsync(CancellationToken ct = default);
    Task SaveComprobanteHabitualAsync(string comprobante, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaTopArticuloDto>> GetTopArticulosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaTopArticuloDto>> DetectarTopArticulosAsync(int limite = 100, CancellationToken ct = default);
    Task SaveTopArticulosAsync(IReadOnlyList<string> idsArticulo, CancellationToken ct = default);
    Task<PuntoVentaEntidadDto?> GetPuntoVentaByIdAsync(int id, CancellationToken ct = default);
    Task<int> SavePuntoVentaAsync(PuntoVentaEntidadSaveRequest request, CancellationToken ct = default);
    /// <summary>Borra el punto de venta y su configuración (sectores, mesas, rubros) si no tiene pedidos, comandas, turnos ni llamados.</summary>
    Task DeletePuntoVentaAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// Deja el sector con <paramref name="cantidad"/> mesas activas (sin contar la barra): reactiva o crea las que faltan y
    /// quita las de número más alto (borra si no tienen historia, da de baja si la tienen, nunca una ocupada).
    /// Con <paramref name="redistribuir"/> reparte todas las mesas en el plano.
    /// </summary>
    Task<PuntoVentaAjusteMesasResultado> AjustarCantidadMesasAsync(int idSector, int cantidad, int? capacidad, bool redistribuir, string usuario, CancellationToken ct = default);
    /// <summary>Borra una mesa sin pedidos, llamados ni reservas.</summary>
    Task DeleteMesaAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<PuntoVentaSectorDto>> GetSectoresAsync(int idPuntoVenta, bool soloActivos = false, CancellationToken ct = default);
    Task<int> SaveSectorAsync(PuntoVentaSectorSaveRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PuntoVentaMesaDto>> GetMesasAsync(int idSector, bool soloActivas = false, CancellationToken ct = default);
    Task<int> SaveMesaAsync(PuntoVentaMesaSaveRequest request, CancellationToken ct = default);
}
