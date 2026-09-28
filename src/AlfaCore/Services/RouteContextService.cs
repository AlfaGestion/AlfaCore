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

    // Misma regla que ConexionClienteService: /consultas/12 es una ruta root de la app, no
    // idweb=consultas/idbase=12 (antes BuildRoute la devolvía sin prefijo tenant).
    internal static bool TryGetSaaSPrefix(string route, out string prefix)
    {
        prefix = string.Empty;
        if (!TenantRouteParser.TryParse(route, out var idWeb, out var baseId))
            return false;

        prefix = $"/{Uri.EscapeDataString(idWeb)}/{baseId}";
        return true;
    }
}
