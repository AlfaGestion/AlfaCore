using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaConfigService
{
    Task<IReadOnlyList<PuntoVentaEntidadDto>> GetPuntosVentaAsync(bool soloActivos = false, CancellationToken ct = default);
    Task<PuntoVentaEntidadDto?> GetPuntoVentaByIdAsync(int id, CancellationToken ct = default);
    Task<int> SavePuntoVentaAsync(PuntoVentaEntidadSaveRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PuntoVentaSectorDto>> GetSectoresAsync(int idPuntoVenta, bool soloActivos = false, CancellationToken ct = default);
    Task<int> SaveSectorAsync(PuntoVentaSectorSaveRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PuntoVentaMesaDto>> GetMesasAsync(int idSector, bool soloActivas = false, CancellationToken ct = default);
    Task<int> SaveMesaAsync(PuntoVentaMesaSaveRequest request, CancellationToken ct = default);
}
