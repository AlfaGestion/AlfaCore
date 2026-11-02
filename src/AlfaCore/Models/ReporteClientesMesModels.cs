namespace AlfaCore.Models;

/// <summary>
/// Ventas por cliente y mes (como «Resumen Cliente / Mes» del VB6), pensado para controlar abonos:
/// marca los meses en que un cliente con facturación automática activa no tuvo factura.
/// </summary>
public sealed class ResumenClientesMesDto
{
    /// <summary>Primer día de cada mes del período, en orden.</summary>
    public IReadOnlyList<DateTime> Meses { get; init; } = [];
    public IReadOnlyList<ResumenClienteMesFilaDto> Filas { get; init; } = [];
    public IReadOnlyList<decimal> TotalesPorMes { get; init; } = [];
    public decimal Total { get; init; }
    public int ClientesConFaltantes => Filas.Count(f => f.MesesSinFacturar > 0);
    public int MesesSinFacturar => Filas.Sum(f => f.MesesSinFacturar);
}

public sealed class ResumenClienteMesFilaDto
{
    public string Cuenta { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    /// <summary>Periodicidad de la facturación automática activa (MENSUAL, BIMESTRAL...), vacía si no tiene.</summary>
    public string Periodicidad { get; init; } = string.Empty;
    public bool FacturacionAutomatica => Periodicidad.Length > 0;
    /// <summary>Total del mes (con IVA, neto de notas de crédito); 0 si no hubo ventas.</summary>
    public IReadOnlyList<decimal> Importes { get; init; } = [];
    public IReadOnlyList<EstadoMesCliente> Estados { get; init; } = [];
    public decimal Total { get; init; }
    public int MesesSinFacturar => Estados.Count(e => e == EstadoMesCliente.SinFacturar);
}

public enum EstadoMesCliente
{
    /// <summary>Sin ventas y sin nada que controlar (cliente sin abono, o mes que no le toca).</summary>
    Vacio,
    Facturado,
    /// <summary>Tiene facturación automática y el mes (o su período) quedó sin factura.</summary>
    SinFacturar,
    /// <summary>Mes en curso todavía sin factura: puede no haberse emitido aún.</summary>
    Pendiente
}
