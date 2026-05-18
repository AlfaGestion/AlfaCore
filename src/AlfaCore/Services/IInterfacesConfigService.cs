using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IInterfacesConfigService
{
    Task<InterfacesUploadSettingsDto> GetUploadSettingsAsync(CancellationToken ct = default);
    Task SaveUploadSettingsAsync(InterfacesUploadSettingsDto settings, CancellationToken ct = default);
    Task<InterfacesCompraIaSettingsDto> GetCompraIaSettingsAsync(CancellationToken ct = default);
    Task SaveCompraIaSettingsAsync(InterfacesCompraIaSettingsDto settings, CancellationToken ct = default);
    Task<InterfacesCompraIaProbeResultDto> ProbeCompraIaSettingsAsync(InterfacesCompraIaSettingsDto settings, CancellationToken ct = default);
}
