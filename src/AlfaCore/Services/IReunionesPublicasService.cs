using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IReunionesPublicasService
{
    Task<ReunionPublicaMesDto> GetPublicMonthAsync(ReunionPublicaMesRequest request, CancellationToken ct = default);
    Task<ReunionPublicaReservaResult> CreateReservationAsync(ReunionPublicaReservaRequest request, CancellationToken ct = default);
    Task<ReunionPublicaAdminDto> GetAdminAsync(CancellationToken ct = default);
    Task<long> SaveTipoAsync(ReunionPublicaTipoDto tipo, string? usuarioAccion = null, CancellationToken ct = default);
    Task CancelReservationAsync(long idReserva, string? usuarioAccion = null, CancellationToken ct = default);
}
