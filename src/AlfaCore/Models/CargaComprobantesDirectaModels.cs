namespace AlfaCore.Models;

/// <summary>Borrador de factura nueva: nunca se persiste como pedido o remito auxiliar.</summary>
public sealed class CargaComprobantesDirecta
{
    public Guid Borrador { get; set; } = Guid.NewGuid();
    public string Tc { get; set; } = "FC";
    public int? FacturaAsociadaId { get; set; }
    public string Cliente { get; set; } = "";
    /// <summary>Null hereda del cliente; vacío selecciona el maestro sin lista.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ListaPrecio { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ClasePrecio { get; set; }
    public string UnidadNegocio { get; set; } = "";
    public string Deposito { get; set; } = "";
    public string Observaciones { get; set; } = "";
    /// <summary>Fecha del comprobante; null = hoy. Electrónicos: ARCA admite hasta 5 días antes o después.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Fecha { get; set; }
    /// <summary>Vendedor del comprobante; null = el del cliente.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Vendedor { get; set; }
    public List<CargaComprobantesDirectaLinea> Lineas { get; set; } = [];
    /// <summary>
    /// Factura generada por la facturación automática (MA_CUENTAS_AUTOCPTES): al grabar actualiza
    /// FechaUltMov y suma una cuota, igual que v_FacturacionAutomatica.vbp. Null en las cargas comunes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FacturacionAutomaticaCuenta { get; set; }
}

public sealed class CargaComprobantesDirectaLinea
{
    /// <summary>Vacío en un renglón de "otros conceptos" (sin artículo): se usa <see cref="Concepto"/>.</summary>
    public string Articulo { get; set; } = "";
    /// <summary>Texto de un renglón de otros conceptos (V_MV_CPTE_OBSERV); requiere PrecioUnitarioNeto.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Concepto { get; set; }
    /// <summary>Otro concepto no gravado (AlicIVA = -1 en Alfa Gestión).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConceptoNoGravado { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool EsConcepto => string.IsNullOrWhiteSpace(Articulo) && Concepto is not null;
    public decimal Cantidad { get; set; } = 1;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Bultos { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PrecioUnitarioNeto { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Descuento { get; set; }
}

/// <summary>Datos del cliente elegido en la carga directa (tarjeta del encabezado).</summary>
public sealed class CargaComprobantesClienteResumen
{
    public string Codigo { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public string DocumentoTipo { get; set; } = "";
    public string Documento { get; set; } = "";
    public string CondicionIva { get; set; } = "";
    public string Domicilio { get; set; } = "";
    public string Localidad { get; set; } = "";
    public string Provincia { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Mail { get; set; } = "";
    public string VendedorCodigo { get; set; } = "";
    public string VendedorNombre { get; set; } = "";
    public string CondicionVenta { get; set; } = "";
    public int DiasVencimiento { get; set; }
    public string ListaCodigo { get; set; } = "";
    public string ListaNombre { get; set; } = "";
    public int Clase { get; set; }
    public string ClaseNombre { get; set; } = "";
    public decimal LimiteCredito { get; set; }
    public bool Bloqueado { get; set; }
}

public sealed record CargaComprobantesPoliticaPrecio(bool PuedeEditarPrecio, bool PuedeEditarDescuento,
    decimal MaximoDescuento, decimal? MaximoDescuentoLinea = null);
