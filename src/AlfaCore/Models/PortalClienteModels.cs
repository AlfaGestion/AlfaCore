namespace AlfaCore.Models;

public sealed class PortalClientePedidosFiltroDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string? Numero { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class PortalClientePedidoResumenDto
{
    public int IdComprobante { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string IdComprobanteTexto { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public decimal Total { get; set; }
    public bool Anulada { get; set; }
    public bool EsPedidoWeb { get; set; }
    public int? IdCatalogoWeb { get; set; }
}

public sealed class PortalClientePedidoDetalleDto
{
    public int IdComprobante { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string IdComprobanteTexto { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string CodigoCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public bool Anulada { get; set; }
    public bool EsPedidoWeb { get; set; }
    public int? IdCatalogoWeb { get; set; }
    public IReadOnlyList<PortalClientePedidoLineaDto> Lineas { get; set; } = [];
}

public sealed class PortalClientePedidoLineaDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
}
