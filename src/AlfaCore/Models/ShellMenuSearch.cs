namespace AlfaCore.Models;

/// <summary>
/// Búsqueda de opciones del menú web (módulos y aplicaciones visibles para el usuario). La comparten
/// el buscador del menú lateral legacy y el buscador del Inicio AlfaDesign, así ambos encuentran
/// exactamente lo mismo y en el mismo orden.
/// </summary>
public static class ShellMenuSearch
{
    public static IReadOnlyList<ShellMenuSearchItemDto> Filter(IEnumerable<ShellMenuSearchItemDto> catalog, string? text, int take = 8)
    {
        var query = text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var tokens = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToArray();
        if (tokens.Length == 0)
            return [];

        var rawQuery = query.ToLowerInvariant();
        return catalog
            .Select(x => new { Item = x, Score = Score(x, rawQuery, tokens) })
            .Where(x => x.Score < int.MaxValue)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Item.ModuloNombre)
            .ThenBy(x => x.Item.Nombre)
            .Take(take)
            .Select(x => x.Item)
            .ToArray();
    }

    private static int Score(ShellMenuSearchItemDto item, string rawQuery, string[] tokens)
    {
        var name = item.Nombre.ToLowerInvariant();
        var description = item.Descripcion.ToLowerInvariant();
        var clave = item.Clave.ToLowerInvariant();
        var observacion = item.Observacion.ToLowerInvariant();
        var full = $"{name} {description} {clave} {observacion}";

        if (!tokens.All(full.Contains))
            return int.MaxValue;

        if (name.StartsWith(rawQuery, StringComparison.OrdinalIgnoreCase))
            return item.EsModulo ? 0 : 1;

        if (name.Contains(rawQuery, StringComparison.OrdinalIgnoreCase))
            return item.EsModulo ? 2 : 3;

        if (description.Contains(rawQuery, StringComparison.OrdinalIgnoreCase))
            return 4;

        return 5;
    }
}
