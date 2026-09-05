namespace AlfaCore.Models;

public static class CotizacionEstados
{
    public const string Borrador = "BORRADOR";
    public const string Enviada = "ENVIADA";
    public const string Aceptada = "ACEPTADA";
    public const string Rechazada = "RECHAZADA";
    public const string Vencida = "VENCIDA";
    public const string Anulada = "ANULADA";
}

public static class CotizacionDetTipos
{
    public const string Articulo = "ARTICULO";
    public const string Tarea = "TAREA";
    public const string Libre = "LIBRE";
    public const string Informativo = "INFORMATIVO";
}

public static class CotizacionOrigenPrecio
{
    public const string Cliente = "CLIENTE";
    public const string ConsumidorFinal = "CONSUMIDOR_FINAL";
    public const string Lista = "LISTA";
    public const string Maestro = "MAESTRO";
    public const string Manual = "MANUAL";
}

/// <summary>Fila de listado (§20/§26/§27 CODEX_RULES: columnas mínimas + paginación server-side).</summary>
public sealed class CotizacionListItemDto
{
    public long IdCotizacion { get; set; }
    public long IdVersion { get; set; }
    public int Numero { get; set; }
    public string TC { get; set; } = "COT";
    public string CodigoVisible => $"{TC}-{Numero:00000000}";
    public int NumeroVersion { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string EmpresaProspecto { get; set; } = string.Empty;
    public string CodigoCliente { get; set; } = string.Empty;
    public string ContactoNombre { get; set; } = string.Empty;
    public string CodigoMoneda { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Estado { get; set; } = CotizacionEstados.Borrador;
    public long? IdOportunidad { get; set; }
    public bool OrigenCrm => IdOportunidad is > 0;
    public bool Vencida => Estado is not (CotizacionEstados.Aceptada or CotizacionEstados.Rechazada or CotizacionEstados.Anulada)
        && FechaVencimiento is { } v && v.Date < DateTime.Today;
}

public sealed class CotizacionListFiltersDto
{
    public string? Texto { get; set; }
    public string? Estado { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string? CodigoCliente { get; set; }
    public bool? SoloOrigenCrm { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>Snapshot completo de una versión: datos comerciales + secciones + líneas.</summary>
public sealed class CotizacionVersionDetailDto
{
    public long IdVersion { get; set; }
    public long IdCotizacion { get; set; }
    public int Numero { get; set; }
    public string TC { get; set; } = "COT";
    public string CodigoVisible => $"{TC}-{Numero:00000000}";
    public int NumeroVersion { get; set; }
    public string EstadoCotizacion { get; set; } = CotizacionEstados.Borrador;
    public string EstadoVersion { get; set; } = CotizacionEstados.Borrador;
    public long? IdOportunidad { get; set; }
    public string? CodigoCliente { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string EmpresaProspecto { get; set; } = string.Empty;
    public string ContactoNombre { get; set; } = string.Empty;
    public string ContactoEmail { get; set; } = string.Empty;
    public string ContactoTelefono { get; set; } = string.Empty;
    public string DocumentoFiscal { get; set; } = string.Empty;
    public string CodigoMoneda { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public string CuerpoPropuesta { get; set; } = string.Empty;
    public decimal DescuentoGeneralPorcentaje { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TotalDescuento { get; set; }
    public decimal Total { get; set; }
    public string? PublicToken { get; set; }
    public List<CotizacionSeccionDto> Secciones { get; set; } = [];
    public List<CotizacionLineaDto> Lineas { get; set; } = [];
    public bool EsBorrador => string.Equals(EstadoVersion, CotizacionEstados.Borrador, StringComparison.OrdinalIgnoreCase);
}

public sealed class CotizacionSeccionDto
{
    public long IdSeccion { get; set; }
    public int Orden { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool MostrarSubtotal { get; set; } = true;
}

public sealed class CotizacionLineaDto
{
    public long IdDetalle { get; set; }
    public long? IdSeccion { get; set; }
    public int Orden { get; set; }
    public string Tipo { get; set; } = CotizacionDetTipos.Libre;
    public string? CodigoRef { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1m;
    public decimal PrecioBase { get; set; }
    public decimal PorcentajeDescuento { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal TasaIva { get; set; }
    public decimal Subtotal { get; set; }
    public bool ImpactaTotal { get; set; } = true;
    public string? OrigenPrecio { get; set; }
}

public sealed class CotizacionCreateRequest
{
    public long? IdOportunidad { get; set; }
    public string? CodigoCliente { get; set; }
    public string? EmpresaProspecto { get; set; }
    public string? ContactoNombre { get; set; }
    public string? ContactoEmail { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? DocumentoFiscal { get; set; }
    public string? CodigoMoneda { get; set; }
    public string? UsuarioAccion { get; set; }
}

public sealed class CotizacionSaveVersionRequest
{
    public long IdVersion { get; set; }
    public string? EmpresaProspecto { get; set; }
    public string? ContactoNombre { get; set; }
    public string? ContactoEmail { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? DocumentoFiscal { get; set; }
    public string? CodigoMoneda { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string? Observaciones { get; set; }
    public string? CuerpoPropuesta { get; set; }
    public decimal DescuentoGeneralPorcentaje { get; set; }
    public List<CotizacionSeccionDto> Secciones { get; set; } = [];
    public List<CotizacionLineaDto> Lineas { get; set; } = [];
    public string? UsuarioAccion { get; set; }
}

/// <summary>Contexto/catálogo del configurador "Alfa Gestión" leído de TA_CONFIGURACION.</summary>
public sealed class CotizacionAlfaConfigDto
{
    public decimal PrecioBase { get; set; }
    public decimal PrecioPorUsuario { get; set; }
    public List<CotizacionAlfaModuloDto> Modulos { get; set; } = [];
    public List<CotizacionAlfaPackReglaDto> Packs { get; set; } = [];
}

public sealed class CotizacionAlfaModuloDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
}

public sealed class CotizacionAlfaPackReglaDto
{
    public int MaxUsuarios { get; set; }
    public int MaxModulos { get; set; }
    public string IdTarea { get; set; } = string.Empty;
}

public sealed class CotizacionAlfaSelectionRequest
{
    public List<string> ModulosCodigo { get; set; } = [];
    public int CantidadUsuarios { get; set; } = 1;
    public string? IdTareaPackElegido { get; set; }
}

/// <summary>Resultado del configurador: líneas listas para agregar a la cotización + el pack recomendado.</summary>
public sealed class CotizacionAlfaResultDto
{
    public List<CotizacionLineaDto> Lineas { get; set; } = [];
    public CotizacionAlfaPackReglaDto? PackRecomendado { get; set; }
    public string? PackRecomendadoDescripcion { get; set; }
}

public sealed class CotizacionTareaDto
{
    public string IdTarea { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal? HorasEstimadas { get; set; }
    public decimal ValorHora { get; set; }
    public decimal TasaIva { get; set; }
    public bool Exento { get; set; }
}

public sealed class CotizacionShareDto
{
    public long IdVersion { get; set; }
    public int IdBase { get; set; }
    public string Token { get; set; } = string.Empty;
    public string RelativeUrl => IdBase > 0 && !string.IsNullOrWhiteSpace(Token)
        ? $"/cotizacion-publica/{IdBase}/{Token}"
        : string.Empty;
}
