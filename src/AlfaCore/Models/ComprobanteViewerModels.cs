namespace AlfaCore.Models;

public sealed class ComprobanteViewerDto
{
    public ComprobanteCabeceraDto? Cabecera { get; init; }
    public IReadOnlyList<ComprobanteInsumoDto> Insumos { get; init; } = [];
    public IReadOnlyList<ComprobanteTareaDto> Tareas { get; init; } = [];
    public IReadOnlyList<ComprobanteObservacionDto> Observaciones { get; init; } = [];
    public IReadOnlyList<ComprobanteDocumentoDto> Documentos { get; init; } = [];
    public IReadOnlyList<ComprobanteAplicacionDto> AplicaA { get; init; } = [];
    public IReadOnlyList<ComprobanteAplicacionDto> AplicadoPor { get; init; } = [];
    public IReadOnlyList<ComprobanteAccionDto> Acciones { get; init; } = [];
}

public sealed class ComprobanteCabeceraDto
{
    public string Tc { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    public int IdComplemento { get; init; }
    public DateTime? Fecha { get; init; }
    public string Cuenta { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string Domicilio { get; init; } = string.Empty;
    public string Localidad { get; init; } = string.Empty;
    public string Telefono { get; init; } = string.Empty;
    public string CodigoPostal { get; init; } = string.Empty;
    public string CondicionComercialCodigo { get; init; } = string.Empty;
    public string CondicionComercialDescripcion { get; init; } = string.Empty;
    public string VendedorCodigo { get; init; } = string.Empty;
    public string VendedorNombre { get; init; } = string.Empty;
    public string TecnicoCodigo { get; init; } = string.Empty;
    public string TecnicoNombre { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public decimal ImporteTotal { get; init; }
    public decimal ImporteSinIva { get; init; }
    public decimal Iva { get; init; }
    public decimal IvaRecargo { get; init; }
    public decimal Descuentos { get; init; }
    public decimal NetoGravado { get; init; }
    public decimal NetoNoGravado { get; init; }
    public bool Anulada { get; init; }
    public bool Finalizada { get; init; }
    public bool Aprobada { get; init; }
    public bool Impresa { get; init; }
    public bool Bloqueada { get; init; }
    public bool Cerrada { get; init; }
    public string ObservacionesGenerales { get; init; } = string.Empty;
    public string Comentarios { get; init; } = string.Empty;
}

public sealed class ComprobanteInsumoDto
{
    public string CodigoArticulo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Unidad { get; init; } = string.Empty;
    public decimal Cantidad { get; init; }
    public decimal PrecioImporte { get; init; }
    public decimal DescuentoImporte { get; init; }
    public decimal Iva { get; init; }
    public decimal Total { get; init; }
    public string NumeroSerie { get; init; } = string.Empty;
    public string NumeroLote { get; init; } = string.Empty;
    public string Deposito { get; init; } = string.Empty;
    public string CodigoBarra { get; init; } = string.Empty;
    public int Secuencia { get; init; }
}

public sealed class ComprobanteTareaDto
{
    public string CodigoTarea { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public decimal Horas { get; init; }
    public decimal ValorHora { get; init; }
    public decimal Total { get; init; }
    public string TecnicoCodigo { get; init; } = string.Empty;
    public string TecnicoNombre { get; init; } = string.Empty;
    public DateTime? FechaEstimadaInicio { get; init; }
    public DateTime? FechaEstimadaFin { get; init; }
    public DateTime? FechaRealInicio { get; init; }
    public DateTime? FechaRealFin { get; init; }
    public string Prioridad { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public int Secuencia { get; init; }
}

public sealed class ComprobanteObservacionDto
{
    public string TipoObservacion { get; init; } = string.Empty;
    public string Observacion { get; init; } = string.Empty;
    public decimal Importe { get; init; }
    public int Secuencia { get; init; }
}

public sealed class ComprobanteDocumentoDto
{
    public string Documento { get; init; } = string.Empty;
    public string NombreArchivo { get; init; } = string.Empty;
    public bool EsUrlExterna { get; init; }
    public bool EsAbrible { get; init; }
}

public sealed class ComprobanteAplicacionDto
{
    public string TcOrigen { get; init; } = string.Empty;
    public string IdComprobanteOrigen { get; init; } = string.Empty;
    public int IdComplementoOrigen { get; init; }
    public string TcRelacionado { get; init; } = string.Empty;
    public string IdComprobanteRelacionado { get; init; } = string.Empty;
    public int IdComplementoRelacionado { get; init; }
    public DateTime? FechaRelacionado { get; init; }
    public decimal ImporteAplicado { get; init; }
    public string Recibo { get; init; } = string.Empty;
    public string EstadoRelacionado { get; init; } = string.Empty;
    public string CuentaRelacionada { get; init; } = string.Empty;
    public string NombreRelacionado { get; init; } = string.Empty;
}

public sealed class ComprobanteAccionDto
{
    public string TipoAccion { get; init; } = string.Empty;
    public string Comentario { get; init; } = string.Empty;
    public DateTime? FechaHora { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public string Pc { get; init; } = string.Empty;
    public string Proceso { get; init; } = string.Empty;
}

public sealed class ComprobanteDocumentoArchivoDto
{
    public string RutaCompleta { get; init; } = string.Empty;
    public string NombreArchivo { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/octet-stream";
}
