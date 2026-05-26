namespace AlfaCore.Models;

public static class ShellNavigationUiHelper
{
    public static string GetNodeDescription(ShellMenuNodeDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Observacion))
            return item.Observacion;
        if (!string.IsNullOrWhiteSpace(item.Descripcion))
            return item.Descripcion;

        var route = item.RutaWeb?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = item.Nombre?.Trim().ToLowerInvariant() ?? string.Empty;

        if (route == "/clientes" || name.Contains("cliente"))
            return "ABM web de clientes.";
        if (route == "/proveedores" || name.Contains("proveedor"))
            return "ABM web de proveedores.";
        if (route == "/contactos" || name.Contains("contacto"))
            return "Agenda web de contactos.";
        if (route.StartsWith("/interfaces"))
            return "Recepción documental web.";
        if (route.StartsWith("/consultas"))
            return "Diseñador de consultas web.";
        if (route.StartsWith("/auditoria"))
            return "Control y trazabilidad operativa.";
        if (route.StartsWith("/tickets"))
            return "Mesa de ayuda y seguimiento.";
        if (route.StartsWith("/conversaciones"))
            return "Inbox operativo de conversaciones.";
        if (route.StartsWith("/calendario"))
            return "Calendario operativo web.";
        if (route.StartsWith("/usuarios"))
            return "Gestión web de usuarios.";
        if (route.StartsWith("/actualizaciones"))
            return "Actualizaciones y mantenimiento de base.";
        if (route.StartsWith("/seguridad/autorizacion-tareas"))
            return "Permisos compartidos entre Desktop y AlfaCore.";
        if (route.StartsWith("/compras"))
            return "Acceso web al circuito de compras.";
        if (route.StartsWith("/ventas"))
            return "Acceso web al circuito de ventas.";
        if (route.StartsWith("/stock"))
            return "Consultas y gestión de stock.";
        if (route.StartsWith("/caja-bancos"))
            return "Consultas operativas de caja y bancos.";
        if (route.StartsWith("/contabilidad"))
            return "Consultas y gestión contable.";

        return "Acceso web disponible dentro de AlfaCore.";
    }

    public static string GetNodeIconClass(ShellMenuNodeDto item)
    {
        var route = item.RutaWeb?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = item.Nombre?.Trim().ToLowerInvariant() ?? string.Empty;

        if (route.StartsWith("/compras") || route == "/proveedores" || name.Contains("proveedor"))
            return "app-card__icon--compras";
        if (route.StartsWith("/ventas") || route == "/clientes" || name.Contains("cliente"))
            return "app-card__icon--ventas";
        if (route.StartsWith("/stock") || name.Contains("stock") || name.Contains("artículo") || name.Contains("articulo"))
            return "app-card__icon--stock";
        if (route.StartsWith("/caja-bancos") || name.Contains("caja") || name.Contains("banco"))
            return "app-card__icon--caja";
        if (route.StartsWith("/contabilidad") || name.Contains("contable") || name.Contains("contabilidad"))
            return "app-card__icon--contabilidad";
        if (route.StartsWith("/consultas") || name.Contains("consulta"))
            return "app-card__icon--consultas";
        if (route.StartsWith("/usuarios") || route.StartsWith("/actualizaciones") || route.StartsWith("/seguridad/") || name.Contains("usuario") || name.Contains("utilidad") || name.Contains("seguridad"))
            return "app-card__icon--usuarios";
        if (route.StartsWith("/contactos") || route.StartsWith("/interfaces") || name.Contains("contacto") || name.Contains("archivo"))
            return "app-card__icon--contactos";
        if (route.StartsWith("/calendario"))
            return "app-card__icon--calendario";
        if (route.StartsWith("/tickets") || route.StartsWith("/auditoria") || route.StartsWith("/conversaciones"))
            return "app-card__icon--tickets";
        if (route.StartsWith("/shell/d010185") || name.Contains("crm"))
            return "app-card__icon--tickets";

        return "app-card__icon--consultas";
    }

    public static string GetModuleIconClass(ShellModuleDto module)
    {
        var key = module.Clave?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = module.Nombre?.Trim().ToLowerInvariant() ?? string.Empty;

        if (key == "D50" || name.Contains("compra"))
            return "app-card__icon--compras";
        if (key == "D60" || name.Contains("venta"))
            return "app-card__icon--ventas";
        if (key == "D80" || name.Contains("stock"))
            return "app-card__icon--stock";
        if (key == "D75" || name.Contains("caja"))
            return "app-card__icon--caja";
        if (key == "D95" || name.Contains("contable") || name.Contains("contabilidad"))
            return "app-card__icon--contabilidad";
        if (key == "D98" || name.Contains("utilidad"))
            return "app-card__icon--usuarios";
        if (key == "D01" || name.Contains("archivo"))
            return "app-card__icon--contactos";
        if (key == "D010185" || name.Contains("crm"))
            return "app-card__icon--tickets";

        return "app-card__icon--consultas";
    }
}
