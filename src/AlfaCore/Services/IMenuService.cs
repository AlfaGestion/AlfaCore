using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IMenuService
{
    Task<IReadOnlyList<ShellModuleDto>> GetModulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ShellMenuSearchItemDto>> GetSearchItemsAsync(CancellationToken ct = default);
    /// <summary>Rutas del menú web que el usuario actual no puede ver (para ocultar solapas del topbar).</summary>
    Task<IReadOnlySet<string>> GetHiddenRoutesAsync(CancellationToken ct = default);
    Task<ShellMenuNodeDto?> GetNodeByRouteAsync(string route, CancellationToken ct = default);
    Task<IReadOnlyList<ShellWorkspaceSectionDto>> GetModuleSectionsAsync(string moduleKey, CancellationToken ct = default);
    Task<ShellModuleDto?> GetModuleByKeyAsync(string moduleKey, CancellationToken ct = default);
    Task<ShellRouteContextDto> GetRouteContextAsync(string route, CancellationToken ct = default);
}
