using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IMenuService
{
    Task<IReadOnlyList<ShellModuleDto>> GetModulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ShellMenuSearchItemDto>> GetSearchItemsAsync(CancellationToken ct = default);
    Task<ShellMenuNodeDto?> GetNodeByRouteAsync(string route, CancellationToken ct = default);
    Task<IReadOnlyList<ShellWorkspaceSectionDto>> GetModuleSectionsAsync(string moduleKey, CancellationToken ct = default);
    Task<ShellModuleDto?> GetModuleByKeyAsync(string moduleKey, CancellationToken ct = default);
    Task<ShellRouteContextDto> GetRouteContextAsync(string route, CancellationToken ct = default);
}
