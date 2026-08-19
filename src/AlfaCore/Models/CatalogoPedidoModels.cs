namespace AlfaCore.Models;

public sealed class CatalogoPedidoConfirmarRequestDto
{
    public int IdInsert { get; set; }
    public string CodigoCliente { get; set; } = string.Empty;
    public string? IdWeb { get; set; }
    public int? IdBase { get; set; }
    public IReadOnlyList<CatalogoPedidoLineaSolicitudDto> Lineas { get; set; } = [];
}

public sealed class CatalogoPedidoLineaSolicitudDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
}

public sealed class CatalogoPedidoResultDto
{
    public string Tc { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public string IdComprobanteTexto { get; set; } = string.Empty;
    public int IdComprobante { get; set; }
    public DateTime Fecha { get; set; }
    public string CodigoCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public IReadOnlyList<CatalogoPedidoLineaResultDto> Lineas { get; set; } = [];
}

public sealed class CatalogoPedidoLineaResultDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
}
