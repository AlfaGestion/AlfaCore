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

    Task<IReadOnlyList<PuntoVentaSectorDto>> GetSectoresAsync(int idPuntoVenta, bool soloActivos = false, CancellationToken ct = default);
    Task<int> SaveSectorAsync(PuntoVentaSectorSaveRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PuntoVentaMesaDto>> GetMesasAsync(int idSector, bool soloActivas = false, CancellationToken ct = default);
    Task<int> SaveMesaAsync(PuntoVentaMesaSaveRequest request, CancellationToken ct = default);
}
