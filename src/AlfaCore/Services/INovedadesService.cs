using AlfaCore.Models;

namespace AlfaCore.Services;

public interface INovedadesService
{
    Task<NovedadesPageDto> GetPageAsync(NovedadesFilterDto filter, long? idNovedad, string usuario, CancellationToken ct = default);
    Task<NovedadDetalleDto?> GetAsync(long idNovedad, string usuario, CancellationToken ct = default);
    Task<long> CreateDraftAsync(string tipo, string usuarioAccion, CancellationToken ct = default);
    Task<long> SaveAsync(NovedadSaveRequest request, CancellationToken ct = default);
    Task ArchiveAsync(long idNovedad, string usuarioAccion, CancellationToken ct = default);
    Task DeleteAsync(long idNovedad, string usuarioAccion, CancellationToken ct = default);
    Task DuplicateAsync(long idNovedad, string usuarioAccion, CancellationToken ct = default);
    Task<NovedadDetalleDto?> GetPendingAnnouncementAsync(string usuario, CancellationToken ct = default);
    Task RegisterViewAsync(long idNovedad, string usuario, CancellationToken ct = default);
    Task MarkReadAsync(long idNovedad, string usuario, CancellationToken ct = default);
    Task<string> SaveCoverImageAsync(Stream content, string fileName, string contentType, string usuarioAccion, CancellationToken ct = default);
}
