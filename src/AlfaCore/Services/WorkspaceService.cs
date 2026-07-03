using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class WorkspaceService(
    IMenuService menuService,
    IFavoritesService favoritesService,
    IRecentService recentService,
    IAppEventService appEvents,
    IActualizacionesService actualizacionesService,
    IAppUserSessionService appUserSession,
    ISessionService sessionService) : IWorkspaceService
{
    private static readonly TimeSpan HomeCacheLifetime = TimeSpan.FromMinutes(5);
    private ShellHomeDto? _cachedHome;
    private string _cachedHomeKey = string.Empty;
    private DateTimeOffset _cachedHomeAt;

    public bool TryGetCachedHome(out ShellHomeDto home)
    {
        var cacheKey = GetHomeCacheKey();
        if (_cachedHome is not null
            && string.Equals(_cachedHomeKey, cacheKey, StringComparison.Ordinal)
            && DateTimeOffset.UtcNow - _cachedHomeAt <= HomeCacheLifetime)
        {
            home = _cachedHome;
            return true;
        }

        home = new ShellHomeDto();
        return false;
    }

    public async Task<ShellHomeDto> GetHomeAsync(CancellationToken ct = default)
    {
        if (TryGetCachedHome(out var cachedHome))
            return cachedHome;

        return await RefreshHomeAsync(ct);
    }

    public async Task<ShellHomeDto> RefreshHomeAsync(CancellationToken ct = default)
    {
        var modules = await menuService.GetModulesAsync(ct);
        if (modules.Count == 0 && await TryAutoRepairShellAsync(ct))
            modules = await menuService.GetModulesAsync(ct);

        var favorites = await SafeGetFavoritesAsync(ct);
        var favoriteKeys = await SafeGetFavoriteKeysAsync(ct);
        IReadOnlyList<ShellMenuSearchItemDto> searchItems = favoriteKeys.Count > 0 ? await menuService.GetSearchItemsAsync(ct) : [];
        var recents = await SafeGetRecentsAsync(ct);

        var home = new ShellHomeDto
        {
            Modules = modules,
            Favorites = MergeFavorites(favorites, favoriteKeys, searchItems),
            Recents = recents
        };

        _cachedHome = home;
        _cachedHomeKey = GetHomeCacheKey();
        _cachedHomeAt = DateTimeOffset.UtcNow;

        return home;
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

    public void InvalidateHomeCache()
    {
        _cachedHome = null;
        _cachedHomeKey = string.Empty;
        _cachedHomeAt = default;
    }

    private string GetHomeCacheKey()
    {
        var activeSession = sessionService.GetActiveSession();
        var user = appUserSession.CurrentUser;

        return string.Join("|",
            activeSession?.Id.ToString("N") ?? string.Empty,
            activeSession?.BaseId.ToString() ?? string.Empty,
            user?.UserName?.Trim().ToUpperInvariant() ?? string.Empty,
            user?.SystemCode?.Trim().ToUpperInvariant() ?? string.Empty,
            user?.SuperAdmin == true ? "1" : "0",
            user?.RequiresInternalLogin == true ? "1" : "0");
    }

    private async Task<bool> TryAutoRepairShellAsync(CancellationToken ct)
    {
        try
        {
            await actualizacionesService.ExecutePendingAsync(new ActualizacionesRunRequest
            {
                UsuarioAccion = appUserSession.GetCurrentUserName("SYSTEM"),
                PcAccion = Environment.MachineName,
                EsEjecucionAutomatica = true
            }, ct);

            return true;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                "Shell",
                "WorkspaceAutoRepairShell",
                ex,
                "No se pudieron ejecutar automáticamente las actualizaciones pendientes del shell web.",
                ct: ct);

            return false;
        }
    }

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
