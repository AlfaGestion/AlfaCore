using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class WorkspaceService(
    IMenuService menuService,
    IFavoritesService favoritesService,
    IRecentService recentService,
    IAppEventService appEvents) : IWorkspaceService
{
    public async Task<ShellHomeDto> GetHomeAsync(CancellationToken ct = default)
    {
        var modules = await menuService.GetModulesAsync(ct);
        var favorites = await SafeGetFavoritesAsync(ct);
        var recents = await SafeGetRecentsAsync(ct);

        return new ShellHomeDto
        {
            Modules = modules,
            Favorites = favorites,
            Recents = recents
        };
    }

    public async Task<ShellWorkspaceDto> GetModuleWorkspaceAsync(string moduleKey, CancellationToken ct = default)
    {
        var module = await menuService.GetModuleByKeyAsync(moduleKey, ct);
        var sections = await menuService.GetModuleSectionsAsync(moduleKey, ct);
        var favorites = await SafeGetFavoritesAsync(ct);
        var recents = await SafeGetRecentsAsync(ct);

        return new ShellWorkspaceDto
        {
            Module = module,
            Sections = sections,
            Favorites = favorites,
            Recents = recents
        };
    }

    public Task<ShellRouteContextDto> GetRouteContextAsync(string route, CancellationToken ct = default)
        => menuService.GetRouteContextAsync(route, ct);

    private async Task<IReadOnlyList<ShellMenuNodeDto>> SafeGetFavoritesAsync(CancellationToken ct)
    {
        try
        {
            return await favoritesService.GetFavoritesAsync(ct);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync("Shell", "SafeGetFavorites", ex, "No se pudieron cargar los favoritos del shell.", ct: ct);
            return [];
        }
    }

    private async Task<IReadOnlyList<ShellMenuNodeDto>> SafeGetRecentsAsync(CancellationToken ct)
    {
        try
        {
            return await recentService.GetRecentsAsync(8, ct);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync("Shell", "SafeGetRecents", ex, "No se pudieron cargar los accesos recientes del shell.", ct: ct);
            return [];
        }
    }
}
