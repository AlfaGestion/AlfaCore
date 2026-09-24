namespace AlfaCore.Models;

public sealed class PuntoVentaContextDto
{
    public string UsuarioActual { get; init; } = string.Empty;
    public string SistemaActual { get; init; } = string.Empty;
    public string SesionSqlActiva { get; init; } = string.Empty;
    public string CajaActual { get; init; } = string.Empty;
    public bool EsAdministrador { get; init; }
    public bool UsaProforma { get; init; } = true;
    public bool VerProformaUsuario { get; init; } = true;
    public string TipoComprobanteDefault { get; init; } = "FC";
    public string SucursalDefault { get; init; } = "0001";
    public string LetrasDisponibles { get; init; } = string.Empty;
    public string FuenteSucursal { get; init; } = string.Empty;
    public bool UsaCajaDefault { get; init; }
}

public sealed class PuntoVentaCatalogFiltersDto
{
    public string Texto { get; set; } = string.Empty;
    public string IdFamilia { get; set; } = string.Empty;
    public string IdRubro { get; set; } = string.Empty;
    public int TamanioPagina { get; set; } = 24;
    public int Pagina { get; set; } = 1;
}

public sealed class PuntoVentaCatalogDto
{
    public IReadOnlyList<PuntoVentaArticleDto> Articulos { get; init; } = [];
    public string ListaPrecioActual { get; init; } = string.Empty;
    public string NombreListaPrecioActual { get; init; } = string.Empty;
    public string ClasePrecioActual { get; init; } = "1";
    public bool UsaPrecioFallback { get; init; }
    public bool TieneMasResultados { get; init; }
    public int PaginaActual { get; init; } = 1;
}

public sealed class PuntoVentaFamilyDto
{
    public string IdFamilia { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
}

public sealed class PuntoVentaArticleDto
{
    public string IdArticulo { get; init; } = string.Empty;
    public string CodigoBarra { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Presentacion { get; init; } = string.Empty;
    public string Procedencia { get; init; } = string.Empty;
    public string IdFamilia { get; init; } = string.Empty;
    public string Familia { get; init; } = string.Empty;
    public string IdUnidad { get; init; } = string.Empty;
    public decimal PrecioUnitario { get; init; }
    public decimal TasaIva { get; init; }
    public bool Exento { get; init; }
    public bool NoControlaStock { get; init; }
    public bool Pesable { get; init; }
    public string RutaImagen { get; init; } = string.Empty;
}

public sealed class PuntoVentaCartItemDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public decimal PrecioUnitario { get; set; }
    public decimal Cantidad { get; set; } = 1m;
    public decimal TasaIva { get; set; }
    public bool Exento { get; set; }
    public bool NoGravado { get; set; }
    public string Familia { get; set; } = string.Empty;
    public decimal Subtotal => Math.Round(Cantidad * PrecioUnitario, 2);
}

public sealed class PuntoVentaArticleImageDto
{
    public string RutaCompleta { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/octet-stream";
    public string NombreArchivo { get; init; } = string.Empty;
}

public sealed class PuntoVentaSettingsDto
{
    public string ClasePrecioDefault { get; set; } = "1";
    public string ComprobanteHabitual { get; set; } = "FC";
    public string SucursalDefault { get; set; } = string.Empty;
    public bool UsaProforma { get; set; } = true;
    public string VerificadorRutaImagenes { get; set; } = string.Empty;
    public string CuentaConsumidorFinal { get; set; } = string.Empty;
    public string CuentaCaja { get; set; } = string.Empty;
    public string CuentaVentasOtrosConceptos { get; set; } = string.Empty;
    public string ClaveCancelar { get; set; } = string.Empty;
    public bool CobranzaPFSoloEfectivo { get; set; }
    public string RutaImagenesLegacy { get; set; } = string.Empty;
    public string EmailServer { get; set; } = string.Empty;
    public string EmailPort { get; set; } = string.Empty;
    public string EmailCuenta { get; set; } = string.Empty;
    public string EmailPassword { get; set; } = string.Empty;
    public string EmailSsl { get; set; } = string.Empty;
    public string FtpCodigoCta { get; set; } = string.Empty;
    public string MedioDePagoContado { get; set; } = string.Empty;
    /// <summary>Si está activo, el recargo de tarjeta se informa como importe sin IVA.</summary>
    public bool RecargoTarjetaSinIva { get; set; }
}

public sealed class PuntoVentaRubroDto
{
    public string IdRubro { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
}

public sealed class PuntoVentaPaymentMethodDto
{
    public string Codigo { get; init; } = string.Empty;
    public string CodigoOpcional { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string MedioDePago { get; init; } = string.Empty;
    public string Moneda { get; init; } = string.Empty;
}

public sealed class PuntoVentaPaymentLineDto
{
    public string CodigoMedioPago { get; set; } = string.Empty;
    public string DescripcionMedioPago { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public decimal Recargo { get; set; }
}

public sealed class PuntoVentaSaleRequestDto
{
    public string CuentaCliente { get; set; } = string.Empty;
    public PuntoVentaClienteEventualDto? ClienteEventual { get; set; }
    public string Vendedor { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Observaciones { get; set; } = string.Empty;
    public string TipoComprobante { get; set; } = "FC";
    public string Sucursal { get; set; } = "0001";
    public string Letra { get; set; } = string.Empty;
    public IReadOnlyList<PuntoVentaCartItemDto> Items { get; init; } = [];
    public IReadOnlyList<PuntoVentaPaymentLineDto> Pagos { get; init; } = [];
    /// <summary>Callback de presentación del POS. No forma parte de la operación SQL.</summary>
    public Func<string, Task>? Progreso { get; init; }
}

public sealed class PuntoVentaClienteEventualDto
{
    public string RazonSocial { get; init; } = string.Empty;
    public string DocumentoTipo { get; init; } = string.Empty;
    public string NumeroDocumento { get; init; } = string.Empty;
    public string Domicilio { get; init; } = string.Empty;
    public string Localidad { get; init; } = string.Empty;
    public string Provincia { get; init; } = string.Empty;
    public string CodigoPostal { get; init; } = string.Empty;
    public string CondicionIva { get; init; } = string.Empty;
}

public sealed class PuntoVentaSaleResultDto
{
    public int IdComprobante { get; init; }
    public int IdCobranza { get; init; }
    public string TipoComprobante { get; init; } = string.Empty;
    public string Sucursal { get; init; } = string.Empty;
    public string Numero { get; init; } = string.Empty;
    public string Letra { get; init; } = string.Empty;
    public string IdComprobanteTexto { get; init; } = string.Empty;
    public decimal Total { get; init; }
    /// <summary>"NoAplica" (facturación electrónica apagada/no configurada), "Aprobado", "Rechazado" o
    /// "Pendiente" (ARCA no respondió y el modo de fallo es DEGRADADO). Ver ArcaCaeEstado.</summary>
    public string CaeEstado { get; init; } = "NoAplica";
    public string? Cae { get; init; }
    public DateTime? CaeVencimiento { get; init; }
    public string? CaeMotivo { get; init; }
}

public sealed class PuntoVentaReceiptEmailRequestDto
{
    public int IdComprobante { get; init; }
    public string TipoComprobante { get; init; } = string.Empty;
    public string IdComprobanteTexto { get; init; } = string.Empty;
    public string Destinatario { get; init; } = string.Empty;
}

public sealed class PuntoVentaReceiptContextDto
{
    public string Empresa { get; init; } = string.Empty;
    public string Direccion { get; init; } = string.Empty;
    public string Telefono { get; init; } = string.Empty;
    public string EmailSugerido { get; init; } = string.Empty;
}

public sealed class PuntoVentaReceiptListItemDto
{
    public int IdComprobante { get; init; }
    public string TipoComprobante { get; init; } = string.Empty;
    public string IdComprobanteTexto { get; init; } = string.Empty;
    public DateTime? FechaHora { get; init; }
    public string Cliente { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public bool Impreso { get; init; }
}

public sealed class PuntoVentaReceiptDataDto
{
    public PuntoVentaSaleResultDto Resultado { get; init; } = new();
    public PuntoVentaReceiptContextDto Contexto { get; init; } = new();
    public IReadOnlyList<PuntoVentaCartItemDto> Items { get; init; } = [];
    public IReadOnlyList<PuntoVentaPaymentLineDto> Pagos { get; init; } = [];
    public bool Impreso { get; init; }
}

public sealed class PuntoVentaCuentaImputacionDto
{
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
}

public sealed class PuntoVentaMovimientoCajaRequestDto
{
    public string Tipo { get; set; } = "I";
    public decimal Importe { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}

public sealed class PuntoVentaMovimientoCajaResultDto
{
    public bool Ok { get; init; }
    public string Mensaje { get; init; } = string.Empty;
}

public sealed class PuntoVentaMovimientoCajaDetalleDto
{
    public string Cuenta { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Detalle { get; init; } = string.Empty;
    public DateTime Fecha { get; init; }
    public string Tc { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    public decimal Importe { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public string TipoMovimiento { get; init; } = string.Empty;
}

public sealed class PuntoVentaConsolidadoCajaDto
{
    public string IdCajas { get; init; } = string.Empty;
    public string Cuenta { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public decimal Ingresos { get; init; }
    public decimal Egresos { get; init; }
    public decimal Saldo { get; init; }
    public string Moneda { get; init; } = string.Empty;
    public decimal Cotizacion { get; init; }
}
