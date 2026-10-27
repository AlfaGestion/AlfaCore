namespace AlfaCore.Models;

public sealed class CargaComprobantesFiltro
{
    public string TipoOrigen { get; set; } = "NP";
    public string Busqueda { get; set; } = string.Empty;
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string Vendedor { get; set; } = string.Empty;
    public string Zona { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Deposito { get; set; } = string.Empty;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class CargaComprobantesContexto
{
    public Guid SesionId { get; set; }
    public string Sistema { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public bool AdminCentral { get; set; }
    public bool PuedeEditarPrecio { get; set; }
    public bool PuedeEditarDescuento { get; set; }
    public bool PuedeCambiarLista { get; set; }
    public bool PuedeCambiarClase { get; set; }
    public IReadOnlyList<CargaComprobantesOpcionFiltro> ListasPrecio { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> ClasesPrecio { get; set; } = [];
    public bool EntregaParcial { get; set; }
    public bool DescargaStockRemitos { get; set; }
    public int Decimales { get; set; } = 2;
    public bool PreciosFinales { get; set; }
    public bool PreciosFinalesNeto { get; set; }
    public bool RevalorizaRemitos { get; set; }
    public bool AjustaRedondeoTotal { get; set; }
    public bool UsaFacturaProforma { get; set; }
    public bool RobaIvaProforma { get; set; }
    public bool ProformaSinRegistracion { get; set; }
    public bool ArtExentoANoGrav { get; set; }
    public decimal AlicuotaGeneral { get; set; }
    public int PuntoVentaElectronico { get; set; }
    public IReadOnlyList<CargaComprobantesTalonario> Talonarios { get; set; } = [];
    public IReadOnlyList<CargaComprobantesTalonario> TalonariosDirecta { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> Vendedores { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> Zonas { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> UnidadesNegocio { get; set; } = [];
    public IReadOnlyList<CargaComprobantesOpcionFiltro> Depositos { get; set; } = [];
}

public sealed class CargaComprobantesOpcionFiltro
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Dato asociado opcional (ej. unidad de negocio de un depósito).</summary>
    public string Grupo { get; set; } = string.Empty;
}

public sealed class CargaComprobantesElectronicaEstado
{
    public string Ambiente { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long UltimaFacturaA { get; set; }
    public long UltimaFacturaB { get; set; }
    public long UltimaFacturaC { get; set; }
}

public sealed class CargaComprobantesElectronicaPrueba
{
    public Guid Operacion { get; set; }
    public int OrigenId { get; set; }
    public string Estado { get; set; } = "";
    public int PuntoVenta { get; set; }
    public int TipoComprobante { get; set; }
    public long Numero { get; set; }
    public decimal Importe { get; set; }
    public string? Cae { get; set; }
    public DateTime? Vencimiento { get; set; }
}

public sealed class CargaComprobantesTalonario
{
    public string Tc { get; set; } = "FC";
    public int Talonario { get; set; }
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Letras { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class CargaComprobantesOrigen
{
    public int Id { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string UnidadNegocio { get; set; } = string.Empty;
    public string Deposito { get; set; } = string.Empty;
    public string Lista { get; set; } = string.Empty;
    public string Moneda { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public bool TieneReservas { get; set; }
    public bool TienePreparacion { get; set; }
    public bool TieneStock { get; set; }
    public bool TieneDevolucion { get; set; }
    public bool TieneOtrosDetalles { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public bool DatosCalculoDisponibles { get; set; }
    public bool ExentoIvaArticulos { get; set; }
    public bool TieneAjustesGlobales { get; set; }
    public bool TienePercepciones { get; set; }
    public IReadOnlyList<CargaComprobantesLineaOrigen> Lineas { get; set; } = [];
}

public sealed class CargaComprobantesLineaOrigen
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PrecioReferenciaNeto { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int ReferenciaOrigenId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal CantidadReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal EquivalenciaReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CostoReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal BultosReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal KilosReferencia { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal MetrosCubicosReferencia { get; set; }
    public int Id { get; set; }
    public int Secuencia { get; set; }
    public string Articulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string Deposito { get; set; } = string.Empty;
    public decimal CantidadOriginal { get; set; }
    public decimal CoeficienteUnidad { get; set; }
    public decimal Pendiente { get; set; }
    public decimal Precio { get; set; }
    public decimal PrecioSinIva { get; set; }
    public decimal AlicuotaIva { get; set; }
    public decimal AlicuotaIvaRecargo { get; set; }
    public decimal ImpuestoInternoUnitario { get; set; }
    public decimal ItcUnitario { get; set; }
    public bool Exento { get; set; }
    public decimal Descuento { get; set; }
    public decimal TotalOriginal { get; set; }
    public int RenglonesArticulo { get; set; }
}

public sealed class CargaComprobantesCantidad
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OrigenId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PrecioUnitarioNeto { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Descuento { get; set; }
    public int LineaId { get; set; }
    public decimal Cantidad { get; set; }
}

public sealed class CargaComprobantesSimulacion
{
    public decimal ImporteReferencia { get; set; }
    public int LineasSeleccionadas { get; set; }
    public string EfectoStock { get; set; } = string.Empty;
    public IReadOnlyList<string> Avisos { get; set; } = [];
    public CargaComprobantesLiquidacion? Liquidacion { get; set; }
    public IReadOnlyList<string> PendientesCalculo { get; set; } = [];
}

/// <summary>Liquidación de artículos con precios históricos; no autoriza emisión.</summary>
public sealed class CargaComprobantesLiquidacion
{
    public decimal NetoGravado { get; set; }
    public decimal NetoNoGravado { get; set; }
    public decimal Iva { get; set; }
    public decimal ImpuestosInternos { get; set; }
    public decimal Total { get; set; }
    public IReadOnlyList<CargaComprobantesLiquidacionLinea> Lineas { get; set; } = [];
    public IReadOnlyList<CargaComprobantesLiquidacionIva> Alicuotas { get; set; } = [];
}

public sealed class CargaComprobantesLiquidacionLinea
{
    public int LineaId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Neto { get; set; }
    public decimal Iva { get; set; }
    public decimal ImpuestosInternos { get; set; }
    public decimal Total { get; set; }
    public bool Exento { get; set; }
    public decimal Alicuota { get; set; }
}

public sealed class CargaComprobantesLiquidacionIva
{
    public decimal Alicuota { get; set; }
    public decimal Neto { get; set; }
    public decimal Iva { get; set; }
}
