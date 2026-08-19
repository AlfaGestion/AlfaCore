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

// Datos de "Mi cuenta": fuente oficial dbo.VT_CLIENTES (misma vista que ya usa el resto del
// Portal Cliente/Clientes). Lista/Clase/Condición/Vendedor son solo informativos: nunca se
// escriben desde esta pantalla.
public sealed class PortalClienteMiCuentaDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string Domicilio { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Provincia { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NombreLista { get; set; } = string.Empty;
    public int Clase { get; set; }
    public string CondicionVenta { get; set; } = string.Empty;
    public string Vendedor { get; set; } = string.Empty;
}

public sealed class PortalClienteActualizarEmailRequestDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class PortalClienteActualizarEmailResultDto
{
    public bool Exito { get; set; }
    public bool SinCambios { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class PortalClienteCambiarClaveRequestDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string ClaveActual { get; set; } = string.Empty;
    public string ClaveNueva { get; set; } = string.Empty;
    public string ConfirmarClaveNueva { get; set; } = string.Empty;
}

public sealed class PortalClienteCambiarClaveResultDto
{
    public bool Exito { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}
