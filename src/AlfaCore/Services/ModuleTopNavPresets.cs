using AlfaCore.Models;
using Microsoft.AspNetCore.Components;

namespace AlfaCore.Services;

/// <summary>
/// Listas de pestañas del topbar AlfaDesign para módulos migrados donde todas las páginas del
/// módulo comparten el mismo conjunto de pestañas (Compras, Ventas, Contabilidad, Caja y Bancos).
/// Evita repetir la misma lista de PageHeaderTopNavItem en cada página del módulo.
/// </summary>
public static class ModuleTopNavPresets
{
    public static IReadOnlyList<PageHeaderTopNavItem> BuildCompras(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("inicio", "Inicio", "/compras"),
            ("proveedores", "Proveedores", "/compras/proveedores"),
            ("comprobantes", "Comprobantes", "/compras/comprobantes"),
            ("rubros", "Rubros", "/compras/rubros"),
            ("familias", "Familias", "/compras/familias"),
            ("articulos", "Artículos", "/compras/articulos"),
            ("actividad", "Actividad", "/compras/actividad"),
            ("informesia", "InformesIA", "/compras/informesia")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildVentas(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("inicio", "Inicio", "/ventas"),
            ("clientes", "Clientes", "/ventas/clientes"),
            ("comprobantes", "Comprobantes", "/ventas/comprobantes"),
            ("rubros", "Rubros", "/ventas/rubros"),
            ("familias", "Familias", "/ventas/familias"),
            ("articulos", "Artículos", "/ventas/articulos"),
            ("comparativo", "Comparativo", "/ventas/comparativo")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildContabilidad(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("resumen", "Resumen", "/contabilidad"),
            ("posicion-iva", "Posición de IVA", "/contabilidad/posicion-iva")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildCajaBancos(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("resumen", "Resumen", "/caja-bancos")
        ]);

    private static IReadOnlyList<PageHeaderTopNavItem> Build(
        IRouteContextService routeContext,
        NavigationManager nav,
        string activeKey,
        (string Key, string Label, string Route)[] items)
        => items.Select(item =>
        {
            var url = routeContext.BuildRoute(item.Route);
            return new PageHeaderTopNavItem
            {
                Key = item.Key,
                Label = item.Label,
                Url = url,
                Active = string.Equals(item.Key, activeKey, StringComparison.OrdinalIgnoreCase),
                OnClick = () =>
                {
                    nav.NavigateTo(url);
                    return Task.CompletedTask;
                }
            };
        }).ToList();
}
