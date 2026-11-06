namespace AlfaCore.Models;

public sealed class CargaComprobantesPlanGrabacion
{
    /// <summary>HOMOLOGACION o PRODUCCION, según el emisor validado (va al registro de la operación).</summary>
    public string AmbienteArca { get; set; } = "HOMOLOGACION";
    // Solo los consolidados guardan partes. NULL mantiene el contrato JSON de operaciones anteriores.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<CargaComprobantesPlanGrabacion>? Origenes { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ArtExentoANoGrav { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<CargaComprobantesPlanGrabacion> Partes => Origenes is { Count: > 0 } ? Origenes : [this];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CargaComprobantesDirecta? Directa { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int ClasePrecio { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SentidoContable { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int SignoStock { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ArcaComprobanteAsociadoDto? Asociado { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AsociadoComprobante { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AsociadoHuella { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string Tc => Directa?.Tc ?? "FC";
    [System.Text.Json.Serialization.JsonIgnore] public bool EsElectronico => AlfaCore.Services.CargaComprobantesTipos.EsElectronico(Tc);
    /// <summary>Sin cuenta contable de comprobante no hay asiento (PR/NP/RM, o proforma con UsaFacturaProformaSinRegistracion).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool GeneraAsiento => TcContable.Length > 0;
    [System.Text.Json.Serialization.JsonIgnore] public int TipoArca => TiposDocumentoCore.Fiscal(TiposDocumentoCore.TipoParaComprobante(Tc, Letra))?.CodigoArca ?? 0;
    public int OrigenId { get; set; }
    public int Talonario { get; set; }
    public string UnidadNegocio { get; set; } = "";
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
    public DateTime Vencimiento { get; set; }
    public string CondicionIva { get; set; } = "";
    public string CondicionVenta { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string Caja { get; set; } = "";
    public string TcContable { get; set; } = "";
    public string Subdiario { get; set; } = "";
    public string Periodo { get; set; } = "";
    public string Letra { get; set; } = "";
    public int PuntoVenta { get; set; }
    public bool DescargaStock { get; set; }
    public string MotivoStock { get; set; } = "";
    public string CuentaCliente { get; set; } = "";
    public string CuentaVentas { get; set; } = "";
    public string CuentaIva { get; set; } = "";
    /// <summary>Cuenta de ventas de otros conceptos (V_TA_INPUT_VENTAS.Otros); solo si hay renglones de concepto.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CuentaOtros { get; set; }
    /// <summary>Cuenta de impuestos internos (V_DFI_IMPINT); solo si la liquidación tiene internos.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CuentaInternos { get; set; }
    /// <summary>Medios de pago validados de una venta de contado: la grabación agrega la cobranza (CBCT/CBFP).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<CargaComprobantesPago>? Cobranza { get; set; }
    public string Huella { get; set; } = "";
    public string HuellaDatos { get; set; } = "";
    public string CuitEmisor { get; set; } = "";
    public CargaComprobantesOrigen Origen { get; set; } = new();
    public CargaComprobantesLiquidacion Liquidacion { get; set; } = new();
    public List<CargaComprobantesCantidad> Cantidades { get; set; } = [];
}

public sealed class CargaComprobantesGrabacionResultado
{
    public Guid? Borrador { get; set; }
    public string Tc { get; set; } = "FC";
    public int? FacturaAsociadaId { get; set; }
    public Guid Operacion { get; set; }
    public int OrigenId { get; set; }
    public string UnidadNegocio { get; set; } = "";
    public string Estado { get; set; } = "";
    public int? FacturaId { get; set; }
    public string? Comprobante { get; set; }
    public int? NumeroAsiento { get; set; }
    /// <summary>Cobranza de contado generada con la factura (CBCT 0003-00000001X).</summary>
    public string? Cobranza { get; set; }
    public string? Cae { get; set; }
    public DateTime? Vencimiento { get; set; }
    public string Mensaje { get; set; } = "";
}
