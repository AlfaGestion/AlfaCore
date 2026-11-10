namespace AlfaCore.Models;

/// <summary>Clave de un comprobante contable (TC, sucursal, número, letra), sin relleno en el TC.</summary>
public sealed record OrdenPagoClave(string Tc, string Sucursal, string Numero, string Letra)
{
    public string Comprobante => Sucursal + Numero + Letra;
}

public sealed record OrdenPagoProveedor(string Codigo, string Nombre, string Documento);

/// <summary>Comprobante a pagar (FCC/NDC/FPC...) o crédito a aplicar (NCC/NCCP/anticipos PG, OPG, PGFP).</summary>
public sealed class OrdenPagoDocumento
{
    public string Tc { get; set; } = "";
    public string Sucursal { get; set; } = "";
    public string Numero { get; set; } = "";
    public string Letra { get; set; } = "";
    public DateTime Fecha { get; set; }
    public DateTime? Vencimiento { get; set; }
    public decimal Saldo { get; set; }
    public string Unidad { get; set; } = "";
    public string Moneda { get; set; } = "";
    public bool Bloqueado { get; set; }
    public string Comprobante => Sucursal + Numero + Letra;
    public OrdenPagoClave Clave => new(Tc, Sucursal, Numero, Letra);
    /// <summary>Motivo por el que no se puede seleccionar (vacío si se puede).</summary>
    public string Motivo { get; set; } = "";
    public bool Seleccionable => Motivo.Length == 0;
}

public sealed class OrdenPagoMedio
{
    public string Cuenta { get; set; } = "";
    public string Descripcion { get; set; } = "";
    /// <summary>EF efectivo, DB banco/transferencia, CH cheque propio.</summary>
    public string Tipo { get; set; } = "";
}

public sealed class OrdenPagoChequera
{
    public string IdBanco { get; set; } = "";
    public string Serie { get; set; } = "";
    public long Desde { get; set; }
    public long Hasta { get; set; }
    public string Cuenta { get; set; } = "";
}

public sealed class OrdenPagoPreparacion
{
    public OrdenPagoProveedor Proveedor { get; set; } = new("", "", "");
    public bool Proforma { get; set; }
    /// <summary>La base usa proformas y el usuario puede verlas: se muestra el interruptor.</summary>
    public bool PuedeProforma { get; set; }
    public bool MezclaProforma { get; set; }
    public string Unidad { get; set; } = "";
    public IReadOnlyList<OrdenPagoDocumento> Comprobantes { get; set; } = [];
    public IReadOnlyList<OrdenPagoDocumento> Creditos { get; set; } = [];
    public IReadOnlyList<OrdenPagoMedio> Medios { get; set; } = [];
    public IReadOnlyList<OrdenPagoChequera> Chequeras { get; set; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> Bancos { get; set; } = [];
    /// <summary>Cheques de terceros en cartera que se pueden entregar como pago.</summary>
    public IReadOnlyList<OrdenPagoChequeCartera> Cartera { get; set; } = [];
    /// <summary>El proveedor está sujeto a retención de Ganancias en esta orden (agente de retención, régimen asignado, sin exención vigente, no proforma).</summary>
    public bool RetieneGanancias { get; set; }
}

/// <summary>Cálculo de la retención de Ganancias de una orden de pago, con el detalle que se muestra al usuario.</summary>
public sealed record OrdenPagoRetencion(
    bool Aplica, string Motivo, string Regimen, decimal Alicuota,
    decimal BaseActual, decimal BaseAnterior, decimal NoImponible, decimal BaseImponible,
    decimal Calculada, decimal RetenidoAntes, decimal Importe)
{
    public static OrdenPagoRetencion NoAplica(string motivo) => new(false, motivo, "", 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record OrdenPagoImporte(OrdenPagoClave Clave, decimal Importe);

/// <summary>Cheque de terceros que está en cartera (recibido en una cobranza y todavía no entregado ni depositado).</summary>
public sealed class OrdenPagoChequeCartera
{
    public string Cuenta { get; set; } = "";
    public string IdBanco { get; set; } = "";
    public string Banco { get; set; } = "";
    public string Numero { get; set; } = "";
    public decimal Importe { get; set; }
    public DateTime? Vencimiento { get; set; }
    public int Clearing { get; set; }
    /// <summary>Código de la cuenta (cliente) que entregó el cheque.</summary>
    public string Origen { get; set; } = "";
    public string Unidad { get; set; } = "";
    public string Clave => $"{Cuenta}|{IdBanco}|{Numero}|{Importe:0.00}";
}

/// <summary>Identifica el cheque de cartera elegido; el resto de sus datos se leen de la base al grabar.</summary>
public sealed record OrdenPagoChequeTercero(string Cuenta, string IdBanco, string Numero, decimal Importe);

public sealed class OrdenPagoMedioLinea
{
    public string Cuenta { get; set; } = "";
    public decimal Importe { get; set; }
    /// <summary>Solo cheque propio.</summary>
    public string IdBanco { get; set; } = "";
    public string Serie { get; set; } = "";
    public string NumeroCheque { get; set; } = "";
    /// <summary>Fecha de pago del cheque (vencimiento del cheque diferido).</summary>
    public DateTime? FechaCheque { get; set; }
    public int Clearing { get; set; }
}

public sealed class OrdenPagoSolicitud
{
    public Guid Operacion { get; set; }
    public string Proveedor { get; set; } = "";
    public DateTime Fecha { get; set; }
    public bool Proforma { get; set; }
    public string Detalle { get; set; } = "";
    /// <summary>Comprobantes a pagar con el importe a pagar de cada uno.</summary>
    public List<OrdenPagoImporte> Comprobantes { get; set; } = [];
    /// <summary>Créditos (NC, anticipos) con el importe que se aplica.</summary>
    public List<OrdenPagoImporte> Creditos { get; set; } = [];
    public List<OrdenPagoMedioLinea> Medios { get; set; } = [];
    /// <summary>Cheques de terceros de la cartera que se entregan al proveedor (su importe suma a los medios de pago).</summary>
    public List<OrdenPagoChequeTercero> ChequesTerceros { get; set; } = [];
    /// <summary>Confirma que lo que sobra de los medios sobre las facturas queda como anticipo del proveedor.</summary>
    public bool ConfirmaAnticipo { get; set; }
    /// <summary>Retención de Ganancias que vio el usuario (el servicio la recalcula al grabar y rechaza si cambió). 0 si no retiene.</summary>
    public decimal Retencion { get; set; }
    /// <summary>Egreso del extracto bancario que origina el pago (Conciliación bancaria). Con él, el único medio es la cuenta bancaria de la importación por el importe del movimiento.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? MovimientoId { get; set; }
}

/// <summary>Egreso bancario listo para pagarle a un proveedor, con el proveedor que se deduce del texto del extracto (por CUIT) si es único.</summary>
public sealed record OrdenPagoMovimiento(long Id, DateTime Fecha, decimal Importe, string CuentaBanco, string CuentaBancoDescripcion,
    string Descripcion, string Categoria, string BusquedaSugerida, OrdenPagoProveedor? ProveedorSugerido);

public sealed record OrdenPagoResultado(Guid Operacion, int Id, string Tc, string Comprobante, int NumeroAsiento, decimal Importe, decimal Anticipo);

/// <summary>Cómo se reparte un pago: aplicación de cada crédito a cada factura y lo que aplica la propia orden de pago.</summary>
public sealed record OrdenPagoReparto(
    IReadOnlyList<(OrdenPagoClave Credito, OrdenPagoClave Factura, decimal Importe)> PorCredito,
    IReadOnlyList<OrdenPagoImporte> Propias,
    decimal Dinero, decimal Creditos, decimal Anticipo);
