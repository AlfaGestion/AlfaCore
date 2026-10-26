namespace AlfaCore.Models;

public sealed class VentasAnalisisFilaDto
{
    public string Codigo { get; init; } = "";
    public string Nombre { get; init; } = "";
    public decimal Total { get; init; }
    public int Comprobantes { get; init; }
    public int Clientes { get; init; }
}

public sealed class VentasAnalisisComprobanteDto
{
    public DateTime Fecha { get; init; }
    public string Tc { get; init; } = "";
    public string IdComprobante { get; init; } = "";
    public string Cuenta { get; init; } = "";
    public string Cliente { get; init; } = "";
    public decimal Total { get; init; }
}

public sealed class VentasAnalisisDto
{
    public IReadOnlyList<VentasAnalisisFilaDto> Filas { get; init; } = [];
    public IReadOnlyList<MonthlyPointDto> Evolucion { get; init; } = [];
    public IReadOnlyList<VentasAnalisisComprobanteDto> Comprobantes { get; init; } = [];
    public int CantidadComprobantes { get; init; }
}
