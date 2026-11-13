using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IActualizacionesService
{
    Task<ActualizacionesDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<ActualizacionesSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(ActualizacionesSettingsDto settings, CancellationToken ct = default);
    Task<int> IgnorarSalteadosAsync(string? usuario, string? pc, CancellationToken ct = default);
    Task<ActualizacionesRunResultDto> ExecutePendingAsync(ActualizacionesRunRequest request, CancellationToken ct = default);
}
