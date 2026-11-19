namespace AlfaCore.Models;

/// <summary>Filtros de /articulos/historial-precios. Origen vacío = maestro de artículos; si no, código de lista de precios.</summary>
public sealed class HistorialPrecioFiltro
{
    public DateTime Desde { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-11);
    public DateTime Hasta { get; set; } = DateTime.Today;
    /// <summary>COSTO o PRECIO1 a PRECIO8.</summary>
    public string Campo { get; set; } = "COSTO";
    public string Origen { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public string RubroCodigo { get; set; } = string.Empty;
    public string MarcaCodigo { get; set; } = string.Empty;
    public string ProveedorCodigo { get; set; } = string.Empty;
    public string ProveedorNombre { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    /// <summary>Vacío, SUBE o BAJA.</summary>
    public string Sentido { get; set; } = string.Empty;
    /// <summary>Solo cambios de al menos este porcentaje (en valor absoluto). 0 = todos.</summary>
    public decimal VariacionMinima { get; set; }
    /// <summary>Desde qué porcentaje un cambio se informa como alerta.</summary>
    public decimal UmbralAlerta { get; set; } = 30;
    public bool SoloActivos { get; set; } = true;
    public string TipoAlerta { get; set; } = string.Empty;
    public string Orden { get; set; } = string.Empty;
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 50;

    public HistorialPrecioFiltro Copiar() => (HistorialPrecioFiltro)MemberwiseClone();
}

public static class HistorialPrecioOrden
{
    public const string FechaDesc = "";
    public const string FechaAsc = "FECHA_ASC";
    public const string VariacionDesc = "PCT_DESC";
    public const string VariacionAsc = "PCT_ASC";
    public const string Articulo = "ARTICULO";
}

public static class HistorialPrecioAlertas
{
    public const string VariacionFuerte = "FUERTE";
    public const string QuedoEnCero = "CERO";
    public const string BajoCosto = "BAJO_COSTO";
    public const string Margen = "MARGEN";
}

public sealed record HistorialPrecioOpcion(string Codigo, string Nombre);

public sealed class HistorialPrecioContextoDto
{
    public IReadOnlyList<HistorialPrecioOpcion> Campos { get; init; } = [];
    public IReadOnlyList<HistorialPrecioOpcion> Listas { get; init; } = [];
    public IReadOnlyList<HistorialPrecioOpcion> Rubros { get; init; } = [];
    public IReadOnlyList<HistorialPrecioOpcion> Marcas { get; init; } = [];
    public int ClaseVenta { get; init; }
}

public sealed class HistorialPrecioPeriodoDto
{
    public DateTime Periodo { get; set; }
    public int Cambios { get; set; }
    public int Articulos { get; set; }
    public int Aumentos { get; set; }
    public int Bajas { get; set; }
    public double? Mediana { get; set; }
    public double? SumaLn { get; set; }
    /// <summary>Variación del período sobre todos los artículos del alcance (los que no cambiaron cuentan 0).</summary>
    public double Variacion { get; set; }
    public double Acumulada { get; set; }
}

public sealed class HistorialPrecioGrupoDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Cambios { get; set; }
    public int Articulos { get; set; }
    public int Aumentos { get; set; }
    public int Bajas { get; set; }
    public double? Mediana { get; set; }
    public double? Variacion { get; set; }
}

public sealed class HistorialPrecioResumenDto
{
    public int Cambios { get; set; }
    public int Articulos { get; set; }
    public int Universo { get; set; }
    public int Aumentos { get; set; }
    public int Bajas { get; set; }
    public int Atipicos { get; set; }
    public double? Mediana { get; set; }
    public double? MayorSuba { get; set; }
    public double? MayorBaja { get; set; }
    public double VariacionAcumulada { get; set; }
    public bool PorDia { get; set; }
    public string CampoComparado { get; set; } = string.Empty;
    public double VariacionComparada { get; set; }
    public IReadOnlyList<HistorialPrecioPeriodoDto> Periodos { get; set; } = [];
    public IReadOnlyList<HistorialPrecioPeriodoDto> PeriodosComparados { get; set; } = [];
    public IReadOnlyList<HistorialPrecioGrupoDto> Rubros { get; set; } = [];
    public IReadOnlyList<HistorialPrecioGrupoDto> Proveedores { get; set; } = [];
    public IReadOnlyList<HistorialPrecioGrupoDto> Usuarios { get; set; } = [];
}

public sealed class HistorialPrecioCambioDto
{
    public int Id { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public decimal Anterior { get; set; }
    public decimal Nuevo { get; set; }
    public double? Pct { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class HistorialPrecioArticuloDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Rubro { get; set; } = string.Empty;
    public string Proveedor { get; set; } = string.Empty;
    public decimal ValorInicial { get; set; }
    public decimal ValorFinal { get; set; }
    public double? Pct { get; set; }
    public int Cambios { get; set; }
    public DateTime UltimoCambio { get; set; }
}

public sealed class HistorialPrecioAlertaDto
{
    public string Tipo { get; set; } = string.Empty;
    public string IdArticulo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }
    public decimal? ValorA { get; set; }
    public decimal? ValorB { get; set; }
    public double? PctA { get; set; }
    public double? PctB { get; set; }
    public string Usuario { get; set; } = string.Empty;
}

public sealed record HistorialPrecioAlertaConteo(string Tipo, int Cantidad);

public sealed class HistorialPrecioPagina<T>
{
    public IReadOnlyList<T> Filas { get; init; } = [];
    public int Total { get; init; }
    public int Pagina { get; init; } = 1;
    public int TamanoPagina { get; init; } = 50;
    public IReadOnlyList<HistorialPrecioAlertaConteo> Conteos { get; init; } = [];
    /// <summary>Precio contra el que se compararon el costo y el margen en las alertas.</summary>
    public string CampoControl { get; init; } = string.Empty;
}

public sealed class HistorialPrecioPuntoDto
{
    public DateTime FechaHora { get; set; }
    public decimal Costo { get; set; }
    public decimal Precio1 { get; set; }
    public decimal Precio2 { get; set; }
    public decimal Precio3 { get; set; }
    public decimal Precio4 { get; set; }
    public decimal Precio5 { get; set; }
    public decimal Precio6 { get; set; }
    public decimal Precio7 { get; set; }
    public decimal Precio8 { get; set; }
    public string IdUnidad { get; set; } = string.Empty;
    public string Moneda { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;

    public decimal Valor(string campo) => campo switch
    {
        "PRECIO1" => Precio1, "PRECIO2" => Precio2, "PRECIO3" => Precio3, "PRECIO4" => Precio4,
        "PRECIO5" => Precio5, "PRECIO6" => Precio6, "PRECIO7" => Precio7, "PRECIO8" => Precio8,
        _ => Costo
    };
}

public sealed class HistorialPrecioFichaDto
{
    public string IdArticulo { get; init; } = string.Empty;
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Proveedor { get; init; } = string.Empty;
    public HistorialPrecioPuntoDto? Actual { get; init; }
    public IReadOnlyList<HistorialPrecioPuntoDto> Puntos { get; init; } = [];
    public bool Truncado { get; init; }
}
