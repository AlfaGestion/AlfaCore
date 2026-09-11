namespace AlfaCore.Models;

public enum PortalClienteComprobantesPendientesFiltro
{
    Todos = 0,
    Vencidos = 1,
    AVencer = 2
}

public sealed class PortalClienteCuentaCorrienteFiltroDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public PortalClienteComprobantesPendientesFiltro Filtro { get; set; } = PortalClienteComprobantesPendientesFiltro.Todos;
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}

public sealed class PortalClienteCuentaCorrienteResumenDto
{
    public decimal SaldoTotal { get; set; }
    public decimal Vencido { get; set; }
    public decimal AVencer { get; set; }
    public int CantidadPendientes { get; set; }
}

public sealed class PortalClienteComprobantePendienteDto
{
    public string Tc { get; set; } = string.Empty;
    public string TcDescripcion { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime Vencimiento { get; set; }
    public decimal Saldo { get; set; }
    public decimal? ImporteOriginal { get; set; }
    public int? IdComprobante { get; set; }
    public bool EstaVencido { get; set; }
}

public sealed class PortalClienteCuentaCorrienteDto
{
    public PortalClienteCuentaCorrienteResumenDto Resumen { get; set; } = new();
    public IReadOnlyList<PortalClienteComprobantePendienteDto> Pendientes { get; set; } = [];
    public IReadOnlyList<PortalClienteCobranzaDto> Cobranzas { get; set; } = [];
}

public sealed class PortalClienteCobranzaDto
{
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string IdComprobante { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public string Detalle { get; set; } = string.Empty;
}

public sealed class PortalClienteEstadoCuentaFiltroDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public bool OcultarSaldoCero { get; set; }
}

public sealed class PortalClienteEstadoCuentaMovimientoDto
{
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public decimal Saldo { get; set; }
    public DateTime? Vencimiento { get; set; }
    public int? IdComprobante { get; set; }

    // Derivado en el mapeo del servicio a partir de Saldo/Vencimiento — no es una columna nueva de
    // cálculo de negocio, solo la misma clasificación visual que ya usa "Comprobantes pendientes"
    // (Vencido/A vencer), agregando "Cancelado" para saldo 0.
    public string Estado { get; set; } = string.Empty;
}

public sealed class PortalClienteEstadoCuentaDto
{
    public decimal SaldoActual { get; set; }
    public decimal SaldoPeriodo { get; set; }
    public IReadOnlyList<PortalClienteEstadoCuentaMovimientoDto> Movimientos { get; set; } = [];
}

public sealed class PortalClienteComprobantePendienteDetalleDto
{
    public int IdComprobante { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string TcDescripcion { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string IdComprobanteTexto { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? Vencimiento { get; set; }
    public string CodigoCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Domicilio { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string DocumentoTipo { get; set; } = string.Empty;
    public string DocumentoNumero { get; set; } = string.Empty;
    public string CondicionIvaDescripcion { get; set; } = string.Empty;
    public string CondicionVentaDescripcion { get; set; } = string.Empty;
    public decimal NetoGravado { get; set; }
    public decimal NetoNoGravado { get; set; }
    public decimal Iva { get; set; }
    public decimal OtrosImpuestos { get; set; }
    public decimal ImporteSinIva { get; set; }
    public decimal ImporteOriginal { get; set; }
    public decimal? SaldoPendiente { get; set; }
    public IReadOnlyList<PortalClienteComprobanteLineaDto> Lineas { get; set; } = [];
}

public sealed class PortalClienteComprobanteLineaDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Bonificacion { get; set; }
    public decimal Iva { get; set; }
    public decimal Subtotal { get; set; }
}
