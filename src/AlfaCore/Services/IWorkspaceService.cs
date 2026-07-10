using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IWorkspaceService
{
    bool TryGetCachedHome(out ShellHomeDto home);
    bool TryGetCachedModuleWorkspace(string moduleKey, out ShellWorkspaceDto workspace);
    Task<ShellHomeDto> GetHomeAsync(CancellationToken ct = default);
    Task<ShellHomeDto> RefreshHomeAsync(CancellationToken ct = default);
    Task<ShellWorkspaceDto> GetModuleWorkspaceAsync(string moduleKey, CancellationToken ct = default);
    Task<ShellWorkspaceDto> RefreshModuleWorkspaceAsync(string moduleKey, CancellationToken ct = default);
    Task<ShellRouteContextDto> GetRouteContextAsync(string route, CancellationToken ct = default);
    void InvalidateHomeCache();
    void InvalidateModuleWorkspaceCache(string? moduleKey = null);
    void InvalidateWorkspaceCache();
}
