using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICrmService
{
    Task<CrmLookupDto> GetLookupsAsync(CancellationToken ct = default);
    Task<PagedResult<CrmOpportunityDto>> SearchAsync(CrmFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<CrmOpportunityDto>> GetKanbanAsync(CrmFilters filters, CancellationToken ct = default);
    Task<CrmOpportunityDetailDto?> GetByIdAsync(long idOportunidad, CancellationToken ct = default);
    Task<long> SaveOpportunityAsync(CrmOpportunitySaveRequest request, CancellationToken ct = default);
    Task QuickUpdateAsync(CrmQuickUpdateRequest request, CancellationToken ct = default);
    Task AddNoteAsync(CrmNotaRequest request, CancellationToken ct = default);
    Task<int> SaveEtapaAsync(CrmEtapaSaveRequest request, CancellationToken ct = default);
    Task DeleteEtapaAsync(int idEtapa, string? usuarioAccion = null, CancellationToken ct = default);
    Task<int> SaveEtiquetaAsync(CrmEtiquetaSaveRequest request, CancellationToken ct = default);
    Task DeleteEtiquetaAsync(int idEtiqueta, string? usuarioAccion = null, CancellationToken ct = default);
    Task<CrmViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, CrmViewSettingsDto settings, CancellationToken ct = default);
}
