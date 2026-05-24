using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAuditoriaService
{
    Task<AuditoriaResumenDto> GetResumenAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AuditoriaErrorRowDto>> SearchErrorsAsync(AuditoriaErrorFilterDto filter, CancellationToken ct = default);
    Task<AuditoriaErrorRowDto?> GetErrorByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUsuariosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetProcesosAsync(CancellationToken ct = default);
    Task<AuditoriaUsuarioLookupsDto> GetUserAuditLookupsAsync(DateTime? desde = null, DateTime? hasta = null, CancellationToken ct = default);
    Task<AuditoriaUsuarioSettingsDto> GetUserAuditSettingsAsync(CancellationToken ct = default);
    Task SaveUserAuditSettingsAsync(AuditoriaUsuarioSettingsDto settings, CancellationToken ct = default);
    Task<AuditoriaUsuarioResultDto> SearchUserAuditAsync(AuditoriaUsuarioFilterDto filter, CancellationToken ct = default);
    Task<AuditoriaComprobanteLookupsDto> GetComprobanteAuditLookupsAsync(DateTime? desde = null, DateTime? hasta = null, CancellationToken ct = default);
    Task<AuditoriaComprobanteResultDto> SearchComprobanteAuditAsync(AuditoriaComprobanteFilterDto filter, CancellationToken ct = default);
}
