using AlfaCore.Models;
using Microsoft.AspNetCore.Components;

namespace AlfaCore.Services;

public sealed class RouteContextService(
    NavigationManager navigationManager,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppModeService appMode) : IRouteContextService
{
    public string BuildRoute(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        if (!appMode.IsSaaSMode)
            return normalizedRoute;

        if (string.IsNullOrWhiteSpace(normalizedRoute))
            return normalizedRoute;

        if (HasSaaSPrefix(normalizedRoute))
            return normalizedRoute;

        var prefix = GetSaaSPrefix();
        if (string.IsNullOrWhiteSpace(prefix))
            return normalizedRoute;

        if (string.Equals(normalizedRoute, "/", StringComparison.Ordinal))
            return prefix;

        return $"{prefix}{normalizedRoute}";
    }

    private string GetSaaSPrefix()
    {
        var currentPath = GetCurrentPath();
        if (TryGetSaaSPrefix(currentPath, out var prefix))
            return prefix;

        var currentUser = appUserSession.CurrentUser;
        var activeSession = sessionService.GetActiveSession();
        var idweb = currentUser?.IdWeb?.Trim();
        if (string.IsNullOrWhiteSpace(idweb) || activeSession is null)
            return string.Empty;

        return $"/{Uri.EscapeDataString(idweb)}/{activeSession.BaseId}";
    }

    private string GetCurrentPath()
        => navigationManager.ToBaseRelativePath(navigationManager.Uri).Split('?')[0].Split('#')[0].Trim('/');

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return string.Empty;

        var trimmed = route.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    private static bool HasSaaSPrefix(string route)
        => TryGetSaaSPrefix(route, out _);

    private static bool TryGetSaaSPrefix(string route, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(route))
            return false;

        var parts = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[1], out _))
            return false;

        prefix = $"/{parts[0]}/{parts[1]}";
        return true;
    }
}
