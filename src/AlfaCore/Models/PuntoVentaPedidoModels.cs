namespace AlfaCore.Models;

public static class PuntoVentaPedidoModalidadKeys
{
    public const string Delivery = "DELIVERY";
    public const string TakeAway = "TAKEAWAY";

    public static readonly string[] All = [Delivery, TakeAway];
}

public static class PuntoVentaPedidoEstadoKeys
{
    public const string Pendiente = "PENDIENTE";
    public const string EnPreparacion = "EN_PREPARACION";
    public const string Listo = "LISTO";
    public const string EnCamino = "EN_CAMINO";
    public const string Entregado = "ENTREGADO";
    public const string Anulado = "ANULADO";

    public static readonly string[] All = [Pendiente, EnPreparacion, Listo, EnCamino, Entregado, Anulado];
    public static readonly string[] Abiertos = [Pendiente, EnPreparacion, Listo, EnCamino];
}

public sealed class PuntoVentaPedidoItemDto
{
    public int Id { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string IdUnidad { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1m;
    public decimal Precio { get; set; }
    public decimal TasaIva { get; set; }
    public bool Exento { get; set; }
    public string Familia { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public decimal Subtotal => Math.Round(Cantidad * Precio, 2);
}

public sealed class PuntoVentaPedidoDto
{
    public int Id { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string IdComprobante { get; set; } = string.Empty;
    public int IdPuntoVenta { get; set; }
    public string Modalidad { get; set; } = PuntoVentaPedidoModalidadKeys.Delivery;
    public string IdCliente { get; set; } = string.Empty;
    public string NombreCliente { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? HorarioProgramado { get; set; }
    public string Estado { get; set; } = PuntoVentaPedidoEstadoKeys.Pendiente;
    public string Observaciones { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime? FechaHoraAlta { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public decimal Total { get; set; }
    public List<PuntoVentaPedidoItemDto> Items { get; set; } = [];
}

public sealed class PuntoVentaPedidoSaveRequest
{
    public int? Id { get; set; }
    public int IdPuntoVenta { get; set; }
    public string Modalidad { get; set; } = PuntoVentaPedidoModalidadKeys.Delivery;
    public string IdCliente { get; set; } = string.Empty;
    public string NombreCliente { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.Today;
    public DateTime? HorarioProgramado { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public List<PuntoVentaPedidoItemDto> Items { get; set; } = [];
    public string? UsuarioAccion { get; set; }
}
