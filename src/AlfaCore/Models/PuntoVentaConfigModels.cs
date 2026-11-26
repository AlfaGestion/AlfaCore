namespace AlfaCore.Models;

public static class PuntoVentaModoKeys
{
    public const string Mostrador = "MOSTRADOR";
    public const string MostradorProduccion = "MOSTRADOR_PRODUCCION";
    public const string Salon = "SALON";
    public const string Delivery = "DELIVERY";

    public static readonly string[] All = [Mostrador, MostradorProduccion, Salon, Delivery];
}

public static class PuntoVentaIngresoRapidoModoKeys
{
    public const string Deshabilitado = "DESHABILITADO";
    public const string Compacto = "COMPACTO";
    public const string Completo = "COMPLETO";
}

public sealed class PuntoVentaCatalogoOpcionDto
{
    public string Valor { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
}

public sealed class PuntoVentaCatalogosDto
{
    public IReadOnlyList<PuntoVentaCatalogoOpcionDto> Sucursales { get; init; } = [];
    public IReadOnlyList<PuntoVentaCatalogoOpcionDto> Cajas { get; init; } = [];
    public IReadOnlyList<PuntoVentaCatalogoOpcionDto> UnidadesNegocio { get; init; } = [];
    public IReadOnlyList<PuntoVentaCatalogoOpcionDto> ListasPrecios { get; init; } = [];
    public IReadOnlyList<PuntoVentaCatalogoOpcionDto> Rubros { get; init; } = [];
}

public sealed class PuntoVentaTopArticuloDto
{
    public int Posicion { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal CantidadVendida { get; set; }
    public decimal TotalVendido { get; set; }
}

public sealed class PuntoVentaEntidadDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Modo { get; set; } = PuntoVentaModoKeys.Mostrador;
    public string Unegocio { get; set; } = string.Empty;
    public string IdCaja { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    /// <summary>Lista de precios del punto de venta. Vacia = la del cliente / configuracion general.</summary>
    public string IdLista { get; set; } = string.Empty;
    /// <summary>Clase de precio 1..8. Null = la de la configuracion general.</summary>
    public int? ClasePrecio { get; set; }
    public bool CargaRapidaGrafica { get; set; }
    public string ModoIngresoRapido { get; set; } = PuntoVentaIngresoRapidoModoKeys.Deshabilitado;
    public decimal ImporteRapido1 { get; set; } = 5m;
    public decimal ImporteRapido2 { get; set; } = 10m;
    public decimal ImporteRapido3 { get; set; } = 25m;
    public decimal ImporteRapido4 { get; set; } = 50m;
    public decimal ImporteRapido5 { get; set; } = 100m;
    public decimal ImporteRapido6 { get; set; } = 200m;
    public decimal CantidadRapida1 { get; set; } = 0.1m;
    public decimal CantidadRapida2 { get; set; } = 0.25m;
    public decimal CantidadRapida3 { get; set; } = 0.5m;
    public decimal CantidadRapida4 { get; set; } = 1m;
    public decimal CantidadRapida5 { get; set; } = 6m;
    public decimal CantidadRapida6 { get; set; } = 12m;
    /// <summary>Rubros que vende el punto de venta. Vacio = todos.</summary>
    public List<string> Rubros { get; set; } = [];
    public bool Activo { get; set; } = true;
    public string Usuario { get; set; } = string.Empty;
    public DateTime? FechaHoraAlta { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
}

public sealed class PuntoVentaEntidadSaveRequest
{
    public int? Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Modo { get; set; } = PuntoVentaModoKeys.Mostrador;
    public string Unegocio { get; set; } = string.Empty;
    public string IdCaja { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public int? ClasePrecio { get; set; }
    public bool CargaRapidaGrafica { get; set; }
    public string ModoIngresoRapido { get; set; } = PuntoVentaIngresoRapidoModoKeys.Deshabilitado;
    public decimal ImporteRapido1 { get; set; } = 5m;
    public decimal ImporteRapido2 { get; set; } = 10m;
    public decimal ImporteRapido3 { get; set; } = 25m;
    public decimal ImporteRapido4 { get; set; } = 50m;
    public decimal ImporteRapido5 { get; set; } = 100m;
    public decimal ImporteRapido6 { get; set; } = 200m;
    public decimal CantidadRapida1 { get; set; } = 0.1m;
    public decimal CantidadRapida2 { get; set; } = 0.25m;
    public decimal CantidadRapida3 { get; set; } = 0.5m;
    public decimal CantidadRapida4 { get; set; } = 1m;
    public decimal CantidadRapida5 { get; set; } = 6m;
    public decimal CantidadRapida6 { get; set; } = 12m;
    public List<string> Rubros { get; set; } = [];
    public bool Activo { get; set; } = true;
    public string? UsuarioAccion { get; set; }
}

public sealed class PuntoVentaSectorDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int IdPuntoVenta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public string Imagen { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class PuntoVentaSectorSaveRequest
{
    public int? Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int IdPuntoVenta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public string Imagen { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;
    public string? UsuarioAccion { get; set; }
}

public sealed class PuntoVentaMesaDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int IdSector { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int? Capacidad { get; set; }
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string Formato { get; set; } = PuntoVentaMesaFormatoKeys.Cuadrada;
    public string Icono { get; set; } = string.Empty;
    public string Imagen { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public sealed class PuntoVentaMesaSaveRequest
{
    public int? Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int IdSector { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int? Capacidad { get; set; }
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string Formato { get; set; } = PuntoVentaMesaFormatoKeys.Cuadrada;
    public string Icono { get; set; } = string.Empty;
    public string Imagen { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public string? UsuarioAccion { get; set; }
}

public static class PuntoVentaMesaFormatoKeys
{
    public const string Cuadrada = "CUADRADA";
    public const string Circular = "CIRCULAR";
    public const string Triangular = "TRIANGULAR";

    public static string Normalizar(string? formato) => formato?.Trim().ToUpperInvariant() switch
    {
        Circular => Circular,
        Triangular => Triangular,
        _ => Cuadrada
    };
}
