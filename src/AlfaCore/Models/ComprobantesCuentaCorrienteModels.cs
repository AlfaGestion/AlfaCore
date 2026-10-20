namespace AlfaCore.Models;

public sealed class ComprobanteCuentaCorrienteDto
{
    public int Orden { get; init; }
    public string Cuenta { get; init; } = "";
    public string RazonSocial { get; init; } = "";
    public DateTime Fecha { get; init; }
    public DateTime? Vencimiento { get; init; }
    public string Tc { get; init; } = "";
    public string IdComprobante { get; init; } = "";
    public decimal Importe { get; init; }
    public decimal Aplicado { get; init; }
    public decimal Pendiente { get; init; }
    public decimal PendienteCliente { get; init; }
    public decimal DiferenciaAplicaciones { get; init; }
    public IReadOnlyList<AplicacionCuentaCorrienteDto> Aplicaciones { get; set; } = [];
}

public sealed class AplicacionCuentaCorrienteDto
{
    public int OrdenDocumento { get; init; }
    public int IdAplicacion { get; init; }
    public string Tc { get; init; } = "";
    public string IdComprobante { get; init; } = "";
    public DateTime? Fecha { get; init; }
    public bool ReferenciaAmbigua { get; init; }
    public decimal Importe { get; init; }
    public decimal SaldoRestante { get; init; }
}

public sealed class ComprobantesCuentaCorrienteResultDto
{
    public IReadOnlyList<ComprobanteCuentaCorrienteDto> Items { get; init; } = [];
    public int TotalRegistros { get; init; }
    public int CantidadClientes { get; init; }
    public decimal ImporteTotal { get; init; }
    public decimal AplicadoTotal { get; init; }
    public decimal PendienteTotal { get; init; }
    public int PaginaActual { get; init; }
    public int TamanioPagina { get; init; }
    public int TotalPaginas => TamanioPagina>0 ? (int)Math.Ceiling((double)TotalRegistros/TamanioPagina) : 0;
}
