namespace AlfaCore.Models;

/// <summary>
/// Operaciones sobre el árbol de permisos que comparten Seguridad &gt; Autorización de tareas y Seguridad &gt;
/// Perfiles. Reglas: lo que viene del perfil (<see cref="MenuAutorizacionItemDto.PorPerfil"/>) no se toca, y los
/// permisos explícitos no se arrastran al tildar un módulo entero (solo se dan tildándolos uno por uno).
/// </summary>
public static class MenuAutorizacionArbol
{
    /// <summary>Tilda o destilda el nodo y sus hijos visibles.</summary>
    public static void Toggle(IReadOnlyList<MenuAutorizacionItemDto> tree, IReadOnlyList<MenuAutorizacionItemDto> visibleTree, string clave, bool check)
    {
        var target = Find(tree, clave);
        if (target is null || !target.Habilitado)
            return;

        var visibleNode = Find(visibleTree, clave);
        foreach (var key in DescendantKeys(visibleNode ?? target))
        {
            var node = Find(tree, key);
            if (node is null || !node.Habilitado || node.PorPerfil)
                continue;
            if (node.Explicito && !string.Equals(key, clave, StringComparison.OrdinalIgnoreCase))
                continue;
            node.Autorizado = check;
        }
    }

    public static MenuAutorizacionItemDto? Find(IEnumerable<MenuAutorizacionItemDto> items, string key)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Clave, key, StringComparison.OrdinalIgnoreCase))
                return item;

            var child = Find(item.Hijos, key);
            if (child is not null)
                return child;
        }

        return null;
    }

    public static List<string> DescendantKeys(MenuAutorizacionItemDto item)
    {
        var keys = new List<string> { item.Clave };
        foreach (var child in item.Hijos)
            keys.AddRange(DescendantKeys(child));
        return keys;
    }

    public static IEnumerable<string> AuthorizedKeys(IEnumerable<MenuAutorizacionItemDto> items)
    {
        foreach (var item in items)
        {
            if (item.Autorizado && item.Habilitado)
                yield return item.Clave;

            foreach (var child in AuthorizedKeys(item.Hijos))
                yield return child;
        }
    }

    public static int Count(IEnumerable<MenuAutorizacionItemDto> items)
        => items.Sum(x => 1 + Count(x.Hijos));

    /// <summary>Marca "Parcial" en los nodos con algunos hijos tildados; tilda el nodo si están todos.</summary>
    public static void NormalizePartial(IEnumerable<MenuAutorizacionItemDto> items)
    {
        foreach (var item in items)
        {
            NormalizePartial(item.Hijos);
            if (item.Hijos.Count == 0)
            {
                item.Parcial = false;
                continue;
            }

            var total = Count(item.Hijos);
            var selected = AuthorizedKeys(item.Hijos).Count();
            item.Parcial = selected > 0 && selected < total;

            if (selected == total && total > 0)
                item.Autorizado = true;
        }
    }

    public static MenuAutorizacionItemDto CloneNode(MenuAutorizacionItemDto item)
        => new()
        {
            Menu = item.Menu,
            Clave = item.Clave,
            Titulo = item.Titulo,
            Nombre = item.Nombre,
            Descripcion = item.Descripcion,
            Proceso = item.Proceso,
            Habilitado = item.Habilitado,
            RutaWeb = item.RutaWeb,
            HabilitadoWeb = item.HabilitadoWeb,
            Autorizado = item.Autorizado,
            Parcial = item.Parcial,
            PorPerfil = item.PorPerfil,
            EsAccion = item.EsAccion,
            Explicito = item.Explicito
        };

    public static List<MenuAutorizacionItemDto> CloneTree(IEnumerable<MenuAutorizacionItemDto> items)
        => items.Select(CloneTreeNode).ToList();

    private static MenuAutorizacionItemDto CloneTreeNode(MenuAutorizacionItemDto item)
    {
        var clone = CloneNode(item);
        foreach (var child in item.Hijos)
            clone.Hijos.Add(CloneTreeNode(child));
        return clone;
    }

    /// <summary>Árbol filtrado por texto: solo opciones web o permisos de acción (más sus padres).</summary>
    public static List<MenuAutorizacionItemDto> Filter(IEnumerable<MenuAutorizacionItemDto> source, string? searchText)
    {
        var term = searchText?.Trim() ?? string.Empty;
        var result = new List<MenuAutorizacionItemDto>();

        foreach (var item in source)
        {
            var children = Filter(item.Hijos, term);
            var matchesSelf = Matches(item, term) && (item.EsWeb || item.EsAccion);
            if (!matchesSelf && children.Count == 0)
                continue;

            var clone = CloneNode(item);
            clone.Hijos.AddRange(children);
            result.Add(clone);
        }

        return result;
    }

    private static bool Matches(MenuAutorizacionItemDto item, string term)
        => string.IsNullOrWhiteSpace(term)
           || string.Join(" ", item.Nombre, item.Clave, item.Descripcion, item.RutaWeb ?? string.Empty)
               .Contains(term, StringComparison.OrdinalIgnoreCase);
}
