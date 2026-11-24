namespace AlfaCore.Models;

/// <summary>
/// Rubros de la página de venta (/sistema). Cada rubro pertenece a una familia y cada familia
/// tiene una base demo (ALFA_CENTRAL.dbo.VentaDemos). Igual que <see cref="LandingContenidoCatalogo"/>,
/// el texto vive en código para publicar rápido; lo operativo (base, usuario, activo) en la tabla.
/// Ver docs/modulos/venta-online.md.
/// </summary>
public sealed class VentaFamilia
{
    /// <summary>Clave de la familia en dbo.VentaDemos.Familia.</summary>
    public string Clave { get; init; } = string.Empty;
    public string Titulo { get; init; } = string.Empty;
    public string Bajada { get; init; } = string.Empty;
    /// <summary>Icono de Bootstrap Icons (ej. "bi-cup-hot").</summary>
    public string Icono { get; init; } = string.Empty;
    /// <summary>Modificador CSS para el color del grupo (landing-rubros.css).</summary>
    public string Tono { get; init; } = string.Empty;
    public IReadOnlyList<VentaRubro> Rubros { get; init; } = [];
}

public sealed record VentaRubro(string Slug, string Nombre);

public sealed record VentaFuncion(string Icono, string Titulo, string Texto);

/// <summary>Fila de ALFA_CENTRAL.dbo.VentaDemos.</summary>
public sealed class VentaDemoDto
{
    public string Familia { get; init; } = string.Empty;
    public int IdBase { get; init; }
    public string CentralLogin { get; init; } = string.Empty;
    public string RutaInicial { get; init; } = string.Empty;
    public string NegocioEjemplo { get; init; } = string.Empty;
    public bool Activo { get; init; }
}

public static class VentaRubrosCatalogo
{
    public const string FamiliaBares = "BARES";
    public const string FamiliaComercio = "COMERCIO";
    public const string FamiliaDistribuidoras = "DISTRIBUIDORAS";
    public const string FamiliaServicios = "SERVICIOS";

    public static readonly IReadOnlyList<VentaFamilia> Familias =
    [
        new VentaFamilia
        {
            Clave = FamiliaBares,
            Titulo = "Bares y cafeterías",
            Bajada = "Mesas, comandas a cocina y barra, carta con QR",
            Icono = "bi-cup-hot",
            Tono = "bares",
            Rubros =
            [
                new("cafeterias", "Cafeterías"),
                new("bares", "Bares"),
                new("restaurantes", "Restaurantes"),
                new("pizzerias", "Pizzerías"),
                new("heladerias", "Heladerías"),
                new("panaderias", "Panaderías y confiterías"),
            ],
        },
        new VentaFamilia
        {
            Clave = FamiliaComercio,
            Titulo = "Comercios",
            Bajada = "Venta rápida con lector, stock y caja",
            Icono = "bi-shop",
            Tono = "comercio",
            Rubros =
            [
                new("tiendas-de-ropa", "Tiendas de ropa"),
                new("ferreterias", "Ferreterías"),
                new("almacenes", "Almacenes y despensas"),
                new("kioscos", "Kioscos y maxikioscos"),
                new("librerias", "Librerías"),
                new("casas-de-electricidad", "Casas de electricidad"),
                new("pet-shops", "Pet shops"),
                new("dieteticas", "Dietéticas"),
            ],
        },
        new VentaFamilia
        {
            Clave = FamiliaDistribuidoras,
            Titulo = "Distribuidoras y mayoristas",
            Bajada = "Listas de precios, cuentas corrientes y cobranzas",
            Icono = "bi-truck",
            Tono = "distribuidoras",
            Rubros =
            [
                new("distribuidoras-de-bebidas", "Distribuidoras de bebidas"),
                new("mayoristas-de-alimentos", "Mayoristas de alimentos"),
                new("distribuidoras-de-limpieza", "Distribuidoras de limpieza"),
                new("corralones", "Corralones"),
            ],
        },
        new VentaFamilia
        {
            Clave = FamiliaServicios,
            Titulo = "Servicios técnicos",
            Bajada = "Tickets, horas, abonos y WhatsApp",
            Icono = "bi-tools",
            Tono = "servicios",
            Rubros =
            [
                new("servicio-tecnico-pc", "Servicio técnico de PC"),
                new("soporte-informatico", "Soporte informático"),
                new("talleres", "Talleres"),
                new("servicios-con-abono", "Empresas de servicios con abono"),
            ],
        },
    ];

    public static readonly IReadOnlyList<VentaFuncion> Funciones =
    [
        new("bi-upc-scan", "Punto de venta rápido", "Lector de códigos, atajos de teclado y cobro en segundos. El stock baja solo."),
        new("bi-receipt", "Factura electrónica", "Facturás A, B y C con ARCA desde el mismo punto de venta. Consultás el CUIT al toque."),
        new("bi-journal-text", "Cuentas corrientes", "Cada cliente con su saldo, sus facturas y sus pagos. Reclamás saldos por WhatsApp."),
        new("bi-grid-3x3", "Salón y cocina", "Mesas, comandas a cocina y barra, y carta con QR para bares y cafeterías."),
        new("bi-bank", "Bancos y Mercado Pago", "Importás los extractos y el sistema te propone qué cobro corresponde a cada factura."),
        new("bi-whatsapp", "WhatsApp y CRM", "Todas las conversaciones de tus clientes en un lugar, con oportunidades y seguimiento."),
    ];

    /// <summary>Familia del rubro (por slug de rubro o por clave/tono de familia). Null si no existe.</summary>
    public static VentaFamilia? BuscarFamilia(string? rubroOFamilia)
    {
        var valor = (rubroOFamilia ?? string.Empty).Trim();
        if (valor.Length == 0)
            return null;

        return Familias.FirstOrDefault(f =>
                   string.Equals(f.Clave, valor, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(f.Tono, valor, StringComparison.OrdinalIgnoreCase)
                   || f.Rubros.Any(r => string.Equals(r.Slug, valor, StringComparison.OrdinalIgnoreCase)));
    }
}
