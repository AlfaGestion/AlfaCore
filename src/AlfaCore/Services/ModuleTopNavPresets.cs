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
            ("reportes", "Reportes", "/compras/reportes"),
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
            ("comparativo", "Comparativo", "/ventas/comparativo"),
            ("punto-venta", "Punto de Venta", "/ventas/punto-venta")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildPuntoVenta(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("punto-venta", "Punto de Venta", "/ventas/punto-venta"),
            ("puntos-venta", "Agregar o editar puntos", "/ventas/puntos-venta"),
            ("informe-punto-venta", "Informe punto de venta", "/ventas/punto-venta/informe")
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
            ("resumen", "Resumen", "/caja-bancos"),
            ("cierre", "Cierre de caja", "/caja-bancos/cierre")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildStock(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("resumen", "Resumen", "/stock")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildCotizaciones(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("cotizaciones", "Cotizaciones", "/cotizaciones"),
            ("configuracion", "Configuración", "/cotizaciones/configuracion")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildCrm(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("oportunidades", "Oportunidades", "/crm")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildTickets(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("mesadeayuda", "Mesa de ayuda", "/tickets")
        ]);

    // Mismas entradas que tenía el sidebar legacy de Auditoría (incluye Usuarios y Autorización).
    public static IReadOnlyList<PageHeaderTopNavItem> BuildAuditoria(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("resumen", "Resumen", "/auditoria"),
            ("errores", "Errores", "/auditoria/errores"),
            ("usuarios", "Actividad de usuarios", "/auditoria/usuarios"),
            ("comprobantes", "Comprobantes", "/auditoria/comprobantes"),
            ("usuarios-sistema", "Usuarios", "/usuarios"),
            ("autorizacion", "Autorización", "/seguridad/autorizacion-tareas")
        ]);

    // Mismas entradas que el sidebar legacy de Actualización de Costos.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildCostos(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("inicio", "Inicio", "/costos"),
            ("nueva", "Nueva importación", "/costos/nueva"),
            ("perfiles", "Perfiles", "/costos/perfiles"),
            ("historial", "Historial", "/costos/historial")
        ]);

    // Mismas entradas que el sidebar legacy de Consultas.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildConsultas(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("consultas", "Mis consultas", "/consultas"),
            ("nueva", "Nueva consulta", "/consultas/nueva")
        ]);

    // Mismas entradas que el sidebar legacy de Interfaces.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildInterfaces(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("recepcion", "Recepción documental", "/interfaces"),
            ("configuracion", "Configuración", "/interfaces/configuracion")
        ]);

    // Carritos de compra (se llega desde Interfaces; conserva el acceso de vuelta).
    public static IReadOnlyList<PageHeaderTopNavItem> BuildCarritoCompras(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("carritos", "Carritos de compra", "/interfaces/carrito-compras"),
            ("interfaces", "Interfaces", "/interfaces")
        ]);

    // Mismas entradas que el sidebar legacy de Actualizaciones.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildActualizaciones(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("base", "Base de datos", "/actualizaciones")
        ]);

    // Tablas de referencia (antes sin menú propio: se llegaba desde Archivos).
    public static IReadOnlyList<PageHeaderTopNavItem> BuildArchivos(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("tablas", "Tablas de referencia", "/archivos/tablas")
        ]);

    // Mismas entradas que el sidebar legacy de Seguridad.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildSeguridad(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("usuarios", "Usuarios", "/usuarios"),
            ("autorizacion", "Autorización de tareas", "/seguridad/autorizacion-tareas"),
            ("auditoria", "Reporte de auditoría", "/auditoria")
        ]);

    // Centro de ayuda.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildAyuda(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("ayuda", "Centro de ayuda", "/ayuda")
        ]);

    // Mismas entradas que el sidebar legacy de Informes.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildInformes(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("articulos", "Artículos", "/informes"),
            ("novedades", "Novedades", "/informes/novedades")
        ]);

    // Mismas entradas que el sidebar legacy de Calendario.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildCalendario(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("guardias", "Calendario", "/calendario"),
            ("reuniones", "Reuniones", "/calendario/reuniones")
        ]);

    // Tareas.
    public static IReadOnlyList<PageHeaderTopNavItem> BuildTareas(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("tareas", "Tareas", "/tareas")
        ]);

    // Partes de horas (tiene varias rutas: /partes-horas, /horas, /crm/partes-horas...).
    public static IReadOnlyList<PageHeaderTopNavItem> BuildPartesHoras(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("partes", "Partes de horas", "/partes-horas")
        ]);

    // Secciones de Carga de viajes por ruta (para sus vistas auxiliares: vista previa del viaje y liquidación).
    public static IReadOnlyList<PageHeaderTopNavItem> BuildCargaViajes(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("viajes", "Viajes", "/carga-viajes"),
            ("tarifas", "Tarifas", "/carga-viajes/tarifas"),
            ("choferes", "Choferes", "/carga-viajes/choferes"),
            ("destinos", "Destinos", "/carga-viajes/destinos"),
            ("tipo-vehiculo", "Tipo vehículo", "/carga-viajes/tipo-vehiculo"),
            ("reportes", "Reportes", "/carga-viajes/reportes"),
            ("liquidaciones", "Liquidaciones", "/carga-viajes/liquidaciones"),
            ("configuracion", "Configuración", "/carga-viajes/configuracion")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildConfiguracionGeneral(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("empresa", "Empresa", "/configuracion-general"),
            ("logo", "Logo y Estilo", "/configuracion-general/logo"),
            ("email", "Email", "/configuracion-general/email"),
            ("ventas", "Ventas", "/configuracion-general/ventas"),
            ("articulos", "Artículos", "/configuracion-general/articulos")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildConfiguracionWebPortal(IRouteContextService routeContext, NavigationManager nav)
        => Build(routeContext, nav, "identidad",
        [
            ("identidad", "Identidad", "/configuracion-web-portal"),
            ("empresa", "Configuración general", "/configuracion-general"),
            ("logo", "Logo de impresión", "/configuracion-general/logo"),
            ("email", "Email saliente", "/configuracion-general/email")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildAdministrar(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("clientes", "Clientes", "/admin"),
            ("modulos", "Módulos", "/admin/modulos"),
            ("cargos", "Cargos", "/admin/cargos"),
            ("pagos", "Pagos", "/admin/pagos"),
            ("consumo-ia", "Consumo IA", "/admin/consumo-ia"),
            ("solicitudes", "Solicitudes", "/admin/solicitudes")
        ]);

    public static IReadOnlyList<PageHeaderTopNavItem> BuildDocumentos(IRouteContextService routeContext, NavigationManager nav, string activeKey)
        => Build(routeContext, nav, activeKey,
        [
            ("disenador", "Diseñador", "/documentos/disenador"),
            ("configuracion", "Configuración", "/documentos/configuracion")
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
