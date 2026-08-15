namespace AlfaCore.Services;

public static class CatalogosUrlHelper
{
    public static string BuildRelativeCatalogUrl(
        int idInsert,
        bool includeClientSuffix,
        string? routeIdWeb,
        int? routeIdBase,
        string? currentUri,
        string? fallbackIdWeb = null,
        int? fallbackIdBase = null)
    {
        var path = includeClientSuffix
            ? $"/catalogo/{idInsert}/cliente"
            : $"/catalogo/{idInsert}";

        var idWeb = NormalizeIdWeb(routeIdWeb)
            ?? NormalizeIdWeb(fallbackIdWeb)
            ?? TryResolveIdWebFromUri(currentUri);

        var idBase = routeIdBase
            ?? fallbackIdBase
            ?? TryResolveIdBaseFromUri(currentUri);

        if (!string.IsNullOrWhiteSpace(idWeb) && idBase.HasValue)
            return $"/{Uri.EscapeDataString(idWeb)}/{idBase.Value}{path}";

        return path;
    }

    private static string? NormalizeIdWeb(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? TryResolveIdWebFromUri(string? currentUri)
    {
        if (!TryGetSaaSPrefixSegments(currentUri, out var idWeb, out _))
            return null;

        return idWeb;
    }

    private static int? TryResolveIdBaseFromUri(string? currentUri)
    {
        if (!TryGetSaaSPrefixSegments(currentUri, out _, out var idBase))
            return null;

        return idBase;
    }

    private static bool TryGetSaaSPrefixSegments(string? currentUri, out string idWeb, out int idBase)
    {
        idWeb = string.Empty;
        idBase = 0;

        if (string.IsNullOrWhiteSpace(currentUri))
            return false;

        if (!Uri.TryCreate(currentUri, UriKind.Absolute, out var uri))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
            return false;

        if (!int.TryParse(segments[1], out idBase))
            return false;

        idWeb = Uri.UnescapeDataString(segments[0]);
        return !string.IsNullOrWhiteSpace(idWeb);
    }
}
