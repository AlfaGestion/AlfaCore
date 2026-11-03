namespace AlfaCore.Models;

/// <summary>Libro de IVA (ventas o compras): un período (normalmente un mes) de una unidad de negocio (un CUIT).</summary>
public sealed class LibroIvaFiltro
{
    public bool Compras { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    /// <summary>Código de unidad de negocio; vacío = todas (solo para consulta: el TXT de ARCA es por CUIT).</summary>
    public string UnidadNegocio { get; set; } = string.Empty;
}

public sealed record LibroIvaAlicuota(decimal Alicuota, decimal Neto, decimal Iva);

public sealed class LibroIvaFila
{
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long Numero { get; set; }
    /// <summary>Código de comprobante de ARCA (1 = Factura A, 6 = B, 11 = C, 3/8/13 = notas de crédito...); 0 si no corresponde.</summary>
    public int CodigoArca { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string CondicionIva { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public decimal NetoGravado { get; set; }
    public decimal NoGravado { get; set; }
    public decimal Exento { get; set; }
    public decimal PercepcionIva { get; set; }
    public decimal PercepcionIibb { get; set; }
    public decimal OtrasPercepciones { get; set; }
    public decimal ImpuestosInternos { get; set; }
    public decimal Total { get; set; }
    public IReadOnlyList<LibroIvaAlicuota> Alicuotas { get; set; } = [];
    public decimal Iva => Alicuotas.Sum(a => a.Iva);
    public decimal IvaDe(decimal alicuota) => Alicuotas.Where(a => a.Alicuota == alicuota).Sum(a => a.Iva);
    public string Comprobante => $"{PuntoVenta:0000}-{Numero:00000000}";
}

public sealed class LibroIvaResultado
{
    public LibroIvaFiltro Filtro { get; set; } = new();
    public string Emisor { get; set; } = string.Empty;
    public string CuitEmisor { get; set; } = string.Empty;
    public string CondicionIvaEmisor { get; set; } = string.Empty;
    public IReadOnlyList<LibroIvaFila> Filas { get; set; } = [];
    public IReadOnlyList<LibroIvaAlicuota> TotalesPorAlicuota { get; set; } = [];
    /// <summary>Fecha de cierre del libro (CIERREVENTAS o CIERRECOMPRAS).</summary>
    public DateTime? Cierre { get; set; }
    public bool PuedeCerrar { get; set; }
    /// <summary>Comprobantes sin código ARCA (no van al TXT del Libro IVA Digital).</summary>
    public int SinCodigoArca { get; set; }
    public decimal NetoGravado => Filas.Sum(f => f.NetoGravado);
    public decimal NoGravado => Filas.Sum(f => f.NoGravado);
    public decimal Exento => Filas.Sum(f => f.Exento);
    public decimal Iva => Filas.Sum(f => f.Iva);
    public decimal Percepciones => Filas.Sum(f => f.PercepcionIva + f.PercepcionIibb + f.OtrasPercepciones);
    public decimal ImpuestosInternos => Filas.Sum(f => f.ImpuestosInternos);
    public decimal Total => Filas.Sum(f => f.Total);
}

public sealed record LibroIvaUnidad(string Codigo, string Descripcion, string Cuit);

public sealed record LibroIvaArchivo(string Nombre, string ContentType, byte[] Contenido);
