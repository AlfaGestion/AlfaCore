using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ITicketsService
{
    Task<TicketLookupDto> GetLookupsAsync(CancellationToken ct = default);
    Task<PagedResult<TicketGridItemDto>> SearchAsync(TicketsFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<TicketGridItemDto>> GetKanbanAsync(TicketsFilters filters, CancellationToken ct = default);
    Task<TicketDetailDto?> GetByIdAsync(long idTicket, CancellationToken ct = default);
    Task<TicketDetailDto?> GetByNumeroAsync(int numero, CancellationToken ct = default);
    Task<long> CreateAsync(TicketCreateRequest request, CancellationToken ct = default);
    Task UpdateAsync(TicketUpdateRequest request, CancellationToken ct = default);
    Task QuickUpdateAsync(TicketQuickUpdateRequest request, CancellationToken ct = default);
    Task AddNoteAsync(TicketNotaRequest request, CancellationToken ct = default);
    Task<TicketViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, TicketViewSettingsDto settings, CancellationToken ct = default);
}
