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
        var favoriteKeys = await SafeGetFavoriteKeysAsync(ct);
        IReadOnlyList<ShellMenuSearchItemDto> searchItems = favoriteKeys.Count > 0 ? await menuService.GetSearchItemsAsync(ct) : [];
        var recents = await SafeGetRecentsAsync(ct);

        return new ShellHomeDto
        {
            Modules = modules,
            Favorites = MergeFavorites(favorites, favoriteKeys, searchItems),
            Recents = recents
        };
    }

    public async Task<ShellWorkspaceDto> GetModuleWorkspaceAsync(string moduleKey, CancellationToken ct = default)
    {
        var module = await menuService.GetModuleByKeyAsync(moduleKey, ct);
        var sections = await menuService.GetModuleSectionsAsync(moduleKey, ct);
        var favorites = await SafeGetFavoritesAsync(ct);
        var favoriteKeys = await SafeGetFavoriteKeysAsync(ct);
        IReadOnlyList<ShellMenuSearchItemDto> searchItems = favoriteKeys.Count > 0 ? await menuService.GetSearchItemsAsync(ct) : [];
        var recents = await SafeGetRecentsAsync(ct);

        return new ShellWorkspaceDto
        {
            Module = module,
            Sections = sections,
            Favorites = MergeFavorites(favorites, favoriteKeys, searchItems),
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

    private async Task<IReadOnlyList<string>> SafeGetFavoriteKeysAsync(CancellationToken ct)
    {
        try
        {
            return await favoritesService.GetFavoriteKeysAsync(ct);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync("Shell", "SafeGetFavoriteKeys", ex, "No se pudieron cargar las claves favoritas del shell.", ct: ct);
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

    private static IReadOnlyList<ShellMenuNodeDto> MergeFavorites(
        IReadOnlyList<ShellMenuNodeDto> favorites,
        IReadOnlyList<string> favoriteKeys,
        IReadOnlyList<ShellMenuSearchItemDto> searchItems)
    {
        if (favoriteKeys.Count == 0)
            return favorites;

        var merged = favorites.ToList();
        var existing = new HashSet<string>(merged.Select(x => x.Clave), StringComparer.OrdinalIgnoreCase);

        foreach (var key in favoriteKeys)
        {
            if (existing.Contains(key))
                continue;

            var searchItem = searchItems.FirstOrDefault(x => string.Equals(x.Clave, key, StringComparison.OrdinalIgnoreCase));
            if (searchItem is null)
                continue;

            merged.Add(new ShellMenuNodeDto
            {
                Menu = searchItem.Menu,
                Clave = searchItem.Clave,
                Nombre = searchItem.Nombre,
                Descripcion = searchItem.Descripcion,
                RutaWeb = searchItem.RutaWeb,
                Icono = searchItem.Icono,
                Observacion = searchItem.Observacion
            });
            existing.Add(key);
        }

        return merged;
    }
}
