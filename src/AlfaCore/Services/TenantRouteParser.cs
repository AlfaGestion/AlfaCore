using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace AlfaCore.Services;

/// <summary>
/// Única regla para decidir si una ruta tiene la forma tenant <c>/{idweb}/{idbase}/...</c>.
/// Antes cada lugar (ConexionClienteService, MainLayout, RouteContextService) aceptaba cualquier
/// <c>/palabra/{int}</c>, así que rutas root propias de la app como <c>/consultas/12</c> o
/// <c>/catalogo/5</c> se interpretaban como "idweb=consultas, idbase=12" y activaban la conexión de
/// OTRA base. Acá un primer segmento que coincide con el primer segmento literal de alguna
/// <c>@page</c> de la app queda reservado: nunca es un idweb.
/// Esto sólo valida la FORMA de la ruta; que el idweb corresponda al idbase lo valida
/// ConexionClienteService contra ALFA_CENTRAL antes de activar cualquier conexión.
/// </summary>
public static class TenantRouteParser
{
    // Prefijos que no son páginas Blazor (endpoints mínimos, assets, hub de Blazor) pero tampoco
    // pueden ser nunca un idweb.
    private static readonly string[] NonPageReservedSegments =
    [
        "api", "v1", "_blazor", "_framework", "_content", "css", "js", "lib", "img", "images", "fonts"
    ];

    private static readonly Lazy<HashSet<string>> ReservedSegments = new(BuildReservedSegments);

    public static IReadOnlyCollection<string> ReservedFirstSegments => ReservedSegments.Value;

    /// <summary>
    /// <paramref name="path"/> es una ruta relativa a la base de la app (con o sin '/' inicial,
    /// con o sin query/fragment). Devuelve true sólo si tiene forma tenant real.
    /// </summary>
    public static bool TryParse(string? path, out string idWeb, out int baseId)
    {
        idWeb = string.Empty;
        baseId = 0;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var clean = path.Split('?')[0].Split('#')[0].Trim('/');
        var segments = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
            return false;

        if (!int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBaseId)
            || parsedBaseId <= 0)
            return false;

        var candidateIdWeb = Uri.UnescapeDataString(segments[0]).Trim();
        if (candidateIdWeb.Length == 0 || IsReservedFirstSegment(candidateIdWeb))
            return false;

        idWeb = candidateIdWeb;
        baseId = parsedBaseId;
        return true;
    }

    public static bool IsReservedFirstSegment(string segment)
        => ReservedSegments.Value.Contains(segment.Trim());

    private static HashSet<string> BuildReservedSegments()
    {
        var reserved = new HashSet<string>(NonPageReservedSegments, StringComparer.OrdinalIgnoreCase);

        foreach (var type in SafeGetTypes(typeof(TenantRouteParser).Assembly))
        {
            foreach (var route in type.GetCustomAttributes<RouteAttribute>(inherit: false))
            {
                var first = route.Template.Trim('/').Split('/', 2)[0].Trim();
                if (first.Length == 0 || first.StartsWith('{'))
                    continue;

                reserved.Add(first);
            }
        }

        return reserved;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
