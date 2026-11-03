namespace AlfaCore.Models;

/// <summary>Un comprobante del archivo «Mis Comprobantes» de ARCA (emitidos o recibidos).</summary>
public sealed class ArcaMisComprobante
{
    public DateTime Fecha { get; set; }
    public int CodigoTipo { get; set; }
    public string TipoTexto { get; set; } = string.Empty;
    /// <summary>FC, NC, ND o RC.</summary>
    public string Clase { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long NumeroDesde { get; set; }
    public long NumeroHasta { get; set; }
    public string Cae { get; set; } = string.Empty;
    public string DocContraparte { get; set; } = string.Empty;
    public string NombreContraparte { get; set; } = string.Empty;
    public string Moneda { get; set; } = "$";
    public decimal TipoCambio { get; set; } = 1;
    public decimal NetoGravado { get; set; }
    public decimal NoGravado { get; set; }
    public decimal Exento { get; set; }
    public decimal OtrosTributos { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    /// <summary>Total en pesos (por el tipo de cambio si es moneda extranjera).</summary>
    public decimal TotalPesos => Moneda is "$" or "PES" or "" ? Total : Math.Round(Total * TipoCambio, 2);
}

public sealed class ArcaMisComprobantesArchivo
{
    /// <summary>true = Recibidos (compras); false = Emitidos (ventas).</summary>
    public bool Recibidos { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public List<ArcaMisComprobante> Comprobantes { get; set; } = [];
}

public enum ControlArcaEstado { Coincide, DifiereImporte, DifiereFecha, OtroPeriodo, FaltaEnSistema, FaltaEnArca }

public sealed class ControlArcaFila
{
    public ControlArcaEstado Estado { get; set; }
    public string Clase { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long Numero { get; set; }
    public long NumeroHasta { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public DateTime? FechaArca { get; set; }
    public DateTime? FechaSistema { get; set; }
    public decimal? TotalArca { get; set; }
    public decimal? TotalSistema { get; set; }
    public decimal Diferencia => (TotalArca ?? 0) - (TotalSistema ?? 0);
    public string Cae { get; set; } = string.Empty;
    /// <summary>Comprobante en el sistema (TC + número) cuando existe.</summary>
    public string ComprobanteSistema { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    public string Comprobante => NumeroHasta > Numero ? $"{PuntoVenta:00000}-{Numero:00000000} a {NumeroHasta:00000000}" : $"{PuntoVenta:00000}-{Numero:00000000}";
}

public sealed class ControlArcaResultado
{
    public bool Recibidos { get; set; }
    public string CuitArchivo { get; set; } = string.Empty;
    public string Emisor { get; set; } = string.Empty;
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public IReadOnlyList<string> Unidades { get; set; } = [];
    /// <summary>Aviso cuando el CUIT del archivo no es el de ninguna unidad de negocio.</summary>
    public string? Aviso { get; set; }
    public IReadOnlyList<ControlArcaFila> Filas { get; set; } = [];
    public int ComprobantesArca { get; set; }
    public int ComprobantesSistema { get; set; }
    public decimal TotalArca { get; set; }
    public decimal TotalSistema { get; set; }
    public int Cantidad(ControlArcaEstado e) => Filas.Count(f => f.Estado == e);
}
