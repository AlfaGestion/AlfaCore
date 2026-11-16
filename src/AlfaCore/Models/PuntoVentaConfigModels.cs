namespace AlfaCore.Models;

public static class PuntoVentaModoKeys
{
    public const string Mostrador = "MOSTRADOR";
    public const string Salon = "SALON";
    public const string Delivery = "DELIVERY";

    public static readonly string[] All = [Mostrador, Salon, Delivery];
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
    public string Icono { get; set; } = string.Empty;
    public string Imagen { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public string? UsuarioAccion { get; set; }
}
