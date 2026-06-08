namespace AlfaCore.Models;

public enum TipoReporteCompras
{
    ResumenDeCompras,
    DetalleDeCompras,
    CuentaCorriente
}

public class FiltrosReporteCompras
{
    public FiltrosReporteCompras()
    {
        var today = DateTime.Today;
        FechaDesde = new DateTime(today.Year, today.Month, 1);
        FechaHasta = today;
    }

    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string? Proveedor { get; set; }
    public string? TipoComprobante { get; set; }
    public int Pagina { get; set; } = 1;
    public int TamanioPagina { get; set; } = 50;
}

public sealed class ReporteComprasOptionsDto
{
    public IReadOnlyList<string> TiposComprobante { get; init; } = [];
}

// ── Resumen de Compras ──────────────────────────────────────────────────────

public sealed class ResumenComprasDto
{
    public decimal TotalComprado { get; init; }
    public decimal NetoTotal { get; init; }
    public decimal IvaTotal { get; init; }
    public int CantidadComprobantes { get; init; }
    public int CantidadProveedores { get; init; }
    public IReadOnlyList<ResumenComprasFilaDto> Filas { get; init; } = [];
}

public sealed class ResumenComprasFilaDto
{
    public string Cuenta { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public int CantidadComprobantes { get; init; }
    public decimal Neto { get; init; }
    public decimal Iva { get; init; }
    public decimal Total { get; init; }
    public decimal Participacion { get; init; }
}

// ── Detalle de Compras ──────────────────────────────────────────────────────

public sealed class DetalleComprasFilaDto
{
    public DateTime Fecha { get; init; }
    public string Tc { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    public string Cuenta { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public decimal Neto { get; init; }
    public decimal Iva { get; init; }
    public decimal Total { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public string EstadoComprobante { get; init; } = string.Empty;
}

public sealed class DetalleComprasResultDto
{
    public IReadOnlyList<DetalleComprasFilaDto> Items { get; init; } = [];
    public int TotalRegistros { get; init; }
    public int PaginaActual { get; init; }
    public int TamanioPagina { get; init; }
    public int TotalPaginas => TamanioPagina > 0 ? (int)Math.Ceiling((double)TotalRegistros / TamanioPagina) : 0;
}

// ── Cuenta Corriente ────────────────────────────────────────────────────────

public sealed class CuentaCorrienteFilaDto
{
    public string Cuenta { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public DateTime Fecha { get; init; }
    public string Tc { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    // 'H' = suma (factura/deuda), 'D' = resta (pago/descuento)
    public string DebeHaber { get; init; } = string.Empty;
    public decimal Importe { get; init; }
    public decimal ImporteSignado { get; init; }
}

public sealed class CuentaCorrienteResultDto
{
    public IReadOnlyList<CuentaCorrienteFilaDto> Items { get; init; } = [];
    public int TotalRegistros { get; init; }
    public int PaginaActual { get; init; }
    public int TamanioPagina { get; init; }
    public decimal SaldoTotal { get; init; }
    public int TotalPaginas => TamanioPagina > 0 ? (int)Math.Ceiling((double)TotalRegistros / TamanioPagina) : 0;
}
