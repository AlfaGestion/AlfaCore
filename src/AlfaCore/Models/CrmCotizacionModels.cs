namespace AlfaCore.Models;

public static class CrmCotizacionEstados
{
    public const string Borrador = "BORRADOR";
    public const string Enviada = "ENVIADA";
    public const string Aceptada = "ACEPTADA";
    public const string Rechazada = "RECHAZADA";
    public const string Vencida = "VENCIDA";
}

public static class CrmCotizacionTipos
{
    public const string Articulos = "ARTICULOS";
    public const string Servicio = "SERVICIO";
}

public class CrmCotizacionDto
{
    public long IdCotizacion { get; set; }
    public int Numero { get; set; }
    public string TC { get; set; } = "CTZ";
    public string CodigoVisible => $"{TC}-{Numero:00000000}";
    public string Tipo { get; set; } = CrmCotizacionTipos.Articulos;
    public string CuerpoServicio { get; set; } = string.Empty;
    public bool EsServicio => string.Equals(Tipo, CrmCotizacionTipos.Servicio, StringComparison.OrdinalIgnoreCase);
    public string UNegocio { get; set; } = string.Empty;
    public long IdOportunidad { get; set; }
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public bool EsConsumidorFinal { get; set; }
    public string IdLista { get; set; } = string.Empty;
    public int ClasePrecio { get; set; } = 1;
    public bool PreciosConIva { get; set; }
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string Estado { get; set; } = CrmCotizacionEstados.Borrador;
    public string Observaciones { get; set; } = string.Empty;
    public decimal TotalNeto { get; set; }
    public decimal TotalIva { get; set; }
    public decimal Total { get; set; }
    public string UsuarioAlta { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public bool Vencida => Estado != CrmCotizacionEstados.Aceptada
        && Estado != CrmCotizacionEstados.Rechazada
        && FechaVencimiento is { } v && v.Date < DateTime.Today;
}

public sealed class CrmCotizacionDetailDto : CrmCotizacionDto
{
    public List<CrmCotizacionLineDto> Lineas { get; set; } = [];
}

public sealed class CrmCotizacionLineDto
{
    public long IdDetalle { get; set; }
    public long IdCotizacion { get; set; }
    public int Orden { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1m;
    public decimal PrecioUnitarioNeto { get; set; }
    public decimal TasaIva { get; set; }
    public decimal PrecioUnitarioConIva { get; set; }
    public decimal SubtotalNeto { get; set; }
    public decimal SubtotalIva { get; set; }
    public decimal Subtotal { get; set; }
}

public sealed class CrmCotizacionSaveRequest
{
    public long IdCotizacion { get; set; }
    public long IdOportunidad { get; set; }
    public string? Tipo { get; set; }
    public string? CuerpoServicio { get; set; }
    public string? UNegocio { get; set; }
    public string? ClienteCodigo { get; set; }
    public string? ClienteNombre { get; set; }
    public string? IdTecnico { get; set; }
    public DateTime? Fecha { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string? Estado { get; set; }
    public string? Observaciones { get; set; }
    public List<CrmCotizacionLineRequest> Lineas { get; set; } = [];
    public string? UsuarioAccion { get; set; }
}

public sealed class CrmCotizacionLineRequest
{
    public string? IdArticulo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1m;
    // Precio unitario tal como lo ve/edita el usuario, expresado en el modo PreciosConIva de la cotización.
    public decimal PrecioUnitario { get; set; }
    public decimal TasaIva { get; set; }
}

// Precio de un artículo ya resuelto para un cliente/consumidor final concreto.
public sealed class CrmCotizacionArticuloDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal PrecioUnitarioNeto { get; set; }
    public decimal TasaIva { get; set; }
    public decimal PrecioUnitarioConIva { get; set; }
}

// Contexto de precios resuelto para un cliente (o consumidor final).
public sealed class CrmCotizacionPricingContextDto
{
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public bool EsConsumidorFinal { get; set; }
    public string IdLista { get; set; } = string.Empty;
    public int ClasePrecio { get; set; } = 1;
    public bool PreciosConIva { get; set; }
    public bool UsaListas { get; set; }
}
