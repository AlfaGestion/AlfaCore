namespace AlfaCore.Models;

/// <summary>Libro de IVA (ventas o compras): un período (normalmente un mes) de una unidad de negocio (un CUIT).</summary>
public sealed class LibroIvaFiltro
{
    public bool Compras { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    /// <summary>Código de unidad de negocio; vacío = todas (solo para consulta: el TXT de ARCA es por CUIT).</summary>
    public string UnidadNegocio { get; set; } = string.Empty;
    /// <summary>Selección de unidades para una presentación consolidada del mismo CUIT.</summary>
    public IReadOnlyList<string> UnidadesNegocio { get; set; } = [];
    public bool SoloSinUnidad { get; set; }
    public bool General { get; set; }
}

public sealed record LibroIvaAlicuota(decimal Alicuota, decimal Neto, decimal Iva);

/// <summary>Columna de importes del detalle del libro (una por alícuota de IVA o por tipo de percepción presente en el período).</summary>
public sealed record LibroIvaColumna(string Titulo, Func<LibroIvaFila, decimal> Valor);

public sealed class LibroIvaFila
{
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long Numero { get; set; }
    /// <summary>Código de comprobante de ARCA (1 = Factura A, 6 = B, 11 = C, 3/8/13 = notas de crédito...); 0 si no corresponde.</summary>
    public int CodigoArca { get; set; }
    /// <summary>Código que grabó el propio asiento (MV_ASIENTOS.CodCpteDGI); se usa cuando el tipo de comprobante no está en la tabla fija de ARCA.</summary>
    public int CodigoArcaAsiento { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    /// <summary>El cliente tiene tipo de documento «Consumidor final»: ARCA no controla el número, así que no se valida (se informa 99 / 0 si no es un DNI o CUIT).</summary>
    public bool ConsumidorFinal { get; set; }
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
    /// <summary>Unidades de negocio que aparecen en el libro, con su CUIT y subtotales.</summary>
    public IReadOnlyList<LibroIvaUnidadResumen> Unidades { get; set; } = [];
    /// <summary>Se pidieron todas las unidades y el libro mezcla más de una: no es el libro de un solo CUIT.</summary>
    public bool Consolidado => Filtro.UnidadNegocio.Length == 0 && Unidades.Count > 1;
    public int CantidadCuits => Unidades.Select(u => new string(u.Cuit.Where(char.IsDigit).ToArray())).Where(c => c.Length > 0).Distinct().Count();
    public decimal NetoGravado => Filas.Sum(f => f.NetoGravado);
    public decimal NoGravado => Filas.Sum(f => f.NoGravado);
    public decimal Exento => Filas.Sum(f => f.Exento);
    public decimal Iva => Filas.Sum(f => f.Iva);
    public decimal Percepciones => Filas.Sum(f => f.PercepcionIva + f.PercepcionIibb + f.OtrasPercepciones);

    /// <summary>Una columna de IVA por cada alícuota que aparece en el período (10,5%, 21%, 27%...). Sin IVA, una sola columna en cero.</summary>
    public IReadOnlyList<LibroIvaColumna> ColumnasIva
    {
        get
        {
            var columnas = Filas.SelectMany(f => f.Alicuotas).Where(a => a.Alicuota > 0 && a.Iva != 0).Select(a => a.Alicuota).Distinct().OrderBy(a => a)
                .Select(a => new LibroIvaColumna($"IVA {a.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-AR"))}%", f => f.IvaDe(a))).ToList();
            return columnas.Count > 0 ? columnas : [new("IVA", f => f.Iva)];
        }
    }

    /// <summary>Una columna por cada tipo de percepción o retención que aparece en el período. Sin ninguna, una sola columna en cero.</summary>
    public IReadOnlyList<LibroIvaColumna> ColumnasPercepciones
    {
        get
        {
            var columnas = new List<LibroIvaColumna>();
            if (Filas.Any(f => f.PercepcionIva != 0)) columnas.Add(new("Percep. IVA", f => f.PercepcionIva));
            if (Filas.Any(f => f.PercepcionIibb != 0)) columnas.Add(new("Percep./Ret. IIBB", f => f.PercepcionIibb));
            if (Filas.Any(f => f.OtrasPercepciones != 0)) columnas.Add(new("Percep. Ganancias/otras", f => f.OtrasPercepciones));
            return columnas.Count > 0 ? columnas : [new("Percep.", f => f.PercepcionIva + f.PercepcionIibb + f.OtrasPercepciones)];
        }
    }

    /// <summary>Columnas de importes del detalle, en orden: neto, no gravado, exento, IVA por alícuota, percepciones por tipo, impuestos internos y total.</summary>
    public IReadOnlyList<LibroIvaColumna> ColumnasImportes =>
    [
        new("Neto gravado", f => f.NetoGravado), new("No gravado", f => f.NoGravado), new("Exento", f => f.Exento),
        .. ColumnasIva, .. ColumnasPercepciones,
        new("Imp. internos", f => f.ImpuestosInternos), new("Total", f => f.Total)
    ];
    public decimal ImpuestosInternos => Filas.Sum(f => f.ImpuestosInternos);
    public decimal Total => Filas.Sum(f => f.Total);
}

public sealed record LibroIvaUnidad(string Codigo, string Descripcion, string Cuit);

public sealed record LibroIvaUnidadResumen(string Codigo, string Nombre, string Cuit, int Cantidad, decimal Neto, decimal Iva, decimal Total);

public sealed record LibroIvaArchivo(string Nombre, string ContentType, byte[] Contenido);
