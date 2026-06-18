using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPartesHorasService
{
    Task<PagedResult<ParteHoraGridItemDto>> SearchAsync(PartesHorasFilters filters, CancellationToken ct = default);
    Task<ParteHoraDetailDto?> GetByIdAsync(long idParteHora, CancellationToken ct = default);
    Task<long> SaveAsync(ParteHoraSaveRequest request, CancellationToken ct = default);
    Task DeleteAsync(long idParteHora, string usuarioAccion, CancellationToken ct = default);
    Task<ParteHoraDashboardDto> GetDashboardAsync(PartesHorasFilters filters, CancellationToken ct = default);
    Task<ParteHoraLookupDto> GetLookupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ParteHoraLookupOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<ParteHoraPersonaOptionDto>> SearchPersonasAsync(string texto, string? clienteCodigo = null, CancellationToken ct = default);
    Task<IReadOnlyList<ParteHoraTicketOptionDto>> SearchTicketsAsync(string texto, string? clienteCodigo = null, CancellationToken ct = default);
    Task<ParteHoraClienteConfigDto?> GetClienteConfigAsync(string clienteCodigo, CancellationToken ct = default);
    Task SaveClienteConfigAsync(ParteHoraClienteConfigDto config, string usuarioAccion, CancellationToken ct = default);
}
