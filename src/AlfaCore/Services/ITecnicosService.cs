using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ITecnicosService
{
    Task<PagedResult<TecnicoGridItemDto>> SearchAsync(TecnicosFilters filters, CancellationToken ct = default);
    Task<TecnicoDetailDto?> GetByIdAsync(string idTecnico, CancellationToken ct = default);
    Task<string> SaveAsync(TecnicoSaveRequest request, CancellationToken ct = default);
    Task DarDeBajaAsync(string idTecnico, CancellationToken ct = default);
    Task<IReadOnlyList<EstadoItemDto>> GetEstadosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUsuariosDisponiblesAsync(CancellationToken ct = default);
    Task<string> GetNextIdTecnicoAsync(CancellationToken ct = default);
    Task<TecnicosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, TecnicosViewSettingsDto settings, CancellationToken ct = default);
}
