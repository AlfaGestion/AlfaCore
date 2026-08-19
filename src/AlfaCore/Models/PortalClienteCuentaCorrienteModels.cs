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

public sealed class PortalClienteComprobantePendienteDetalleDto
{
    public int IdComprobante { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string TcDescripcion { get; set; } = string.Empty;
    public string IdComprobanteTexto { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? Vencimiento { get; set; }
    public string CodigoCliente { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public decimal ImporteOriginal { get; set; }
    public decimal? SaldoPendiente { get; set; }
    public IReadOnlyList<PortalClientePedidoLineaDto> Lineas { get; set; } = [];
}
