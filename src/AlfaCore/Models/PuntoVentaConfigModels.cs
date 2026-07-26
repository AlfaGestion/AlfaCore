namespace AlfaCore.Models;

public static class PuntoVentaModoKeys
{
    public const string Mostrador = "MOSTRADOR";
    public const string Salon = "SALON";
    public const string Delivery = "DELIVERY";

    public static readonly string[] All = [Mostrador, Salon, Delivery];
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
