using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IWorkspaceService
{
    Task<ShellHomeDto> GetHomeAsync(CancellationToken ct = default);
    Task<ShellWorkspaceDto> GetModuleWorkspaceAsync(string moduleKey, CancellationToken ct = default);
    Task<ShellRouteContextDto> GetRouteContextAsync(string route, CancellationToken ct = default);
}
