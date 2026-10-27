namespace AlfaCore.Models;

public enum TipoReporteCompras
{
    ResumenDeCompras,
    DetalleDeCompras,
    CuentaCorriente,
    Saldos,
    ControlCredito,
    CuentaCorrienteResumida,
    CuentaCorrienteAnalitica
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
    public string? CuentaExacta { get; set; }
    public string? Provincia { get; set; }
    public bool IncluirProforma { get; set; }
    public string? TipoComprobante { get; set; }
    public string? Vendedor { get; set; }
    public bool SinVendedor { get; set; }
    public string? Zona { get; set; }
    public bool SinZona { get; set; }
    public bool IncluirSaldosCero { get; set; }
    public string[] UnidadesNegocio { get; set; } = [];
    public int Pagina { get; set; } = 1;
    public int TamanioPagina { get; set; } = 50;
}

public sealed class ReporteComprasOptionsDto
{
    public IReadOnlyList<string> TiposComprobante { get; init; } = [];
    public IReadOnlyList<ReporteFiltroOpcion> Vendedores { get; init; } = [];
    public IReadOnlyList<ReporteFiltroOpcion> UnidadesNegocio { get; init; } = [];
    public bool PuedeIncluirProforma { get; init; }
    public IReadOnlyList<ReporteFiltroOpcion> Provincias { get; init; } = [];
    public IReadOnlyList<string> Zonas { get; init; } = [];
}

public sealed record ReporteFiltroOpcion(string Codigo, string Nombre);

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
    public string DescMotivo { get; init; } = string.Empty;
    public decimal Neto { get; init; }
    public decimal Iva { get; init; }
    public decimal Total { get; init; }
    public string Usuario { get; init; } = string.Empty;
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
    // Clientes: D suma/H resta. Proveedores: H suma/D resta.
    public string DebeHaber { get; init; } = string.Empty;
    public decimal Importe { get; init; }
    public decimal ImporteSignado { get; init; }
    public bool EsSaldoInicial { get; init; }
    public decimal SaldoInicial { get; init; }
    public decimal SaldoAnteriorLinea { get; init; }
    public decimal SaldoParcial { get; init; }
    public decimal SaldoFinalCuenta { get; init; }
    public bool UltimaLineaCuenta { get; init; }
}

public sealed class CuentaCorrienteResultDto
{
    public IReadOnlyList<CuentaCorrienteFilaDto> Items { get; init; } = [];
    public int TotalRegistros { get; init; }
    public int PaginaActual { get; init; }
    public int TamanioPagina { get; init; }
    public decimal SaldoTotal { get; init; }
    public decimal SaldoInicialTotal { get; init; }
    public decimal SaldoFinalTotal { get; init; }
    public int TotalPaginas => TamanioPagina > 0 ? (int)Math.Ceiling((double)TotalRegistros / TamanioPagina) : 0;
}
