namespace AlfaCore.Models;

/// <summary>Fila del visor de órdenes de pago (OPG) y pagos proforma (PGFP) ya cargados.</summary>
public sealed class OrdenPagoFila
{
    public int Id { get; set; }
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string Cuenta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Unidad { get; set; } = "";
    public decimal Importe { get; set; }
    public bool Anulada { get; set; }
    /// <summary>OPG sin medios y todavía sin liquidar (el legacy la deja con Finalizada = 0).</summary>
    public bool Pendiente { get; set; }
    /// <summary>Grabada desde AlfaCore (tiene su operación en ALFACORE_PAGO_OPERACION); si no, viene del sistema legacy.</summary>
    public bool Web { get; set; }
    public string? UltimoEnvio { get; set; }
    public string Estado => Anulada ? "Anulada" : Pendiente ? "Pendiente de liquidar" : "Pagada";
}

public sealed class OrdenPagoCancelado
{
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public DateTime? Fecha { get; set; }
    /// <summary>Lo que canceló la propia orden con sus medios de pago.</summary>
    public decimal PorOrden { get; set; }
    /// <summary>Lo que se canceló con créditos aplicados (notas de crédito, anticipos) a través de esta orden.</summary>
    public decimal PorCreditos { get; set; }
    public decimal Total => PorOrden + PorCreditos;
}

public sealed class OrdenPagoMedioDetalle
{
    public string Cuenta { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Importe { get; set; }
    public string Cheque { get; set; } = "";
    public DateTime? Vencimiento { get; set; }
}

/// <summary>Un comprobante contra el que se aplicó un crédito o anticipo.</summary>
public sealed class OrdenPagoRelacion
{
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    /// <summary>Orden o pago por el que se hizo la aplicación (vacío si es esta misma).</summary>
    public string Orden { get; set; } = "";
    public decimal Importe { get; set; }
}

public sealed class OrdenPagoAsientoLinea
{
    public int Asiento { get; set; }
    public int Secuencia { get; set; }
    public string Cuenta { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string DebeHaber { get; set; } = "";
    public decimal Importe { get; set; }
}

public sealed class OrdenPagoEvento
{
    public DateTime Fecha { get; set; }
    public string Tipo { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Pc { get; set; } = "";
    public string Detalle { get; set; } = "";
}

/// <summary>Detalle de una orden de pago: qué pagó, con qué, de dónde salieron los créditos, asiento e historial.</summary>
public sealed class OrdenPagoDetalle
{
    public OrdenPagoFila Cabecera { get; set; } = new();
    public string Documento { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Comentarios { get; set; } = "";
    public DateTime? Grabada { get; set; }
    public List<OrdenPagoCancelado> Cancelados { get; set; } = [];
    public List<OrdenPagoMedioDetalle> Medios { get; set; } = [];
    /// <summary>Créditos (notas de crédito, anticipos) que se aplicaron a las facturas a través de esta orden.</summary>
    public List<OrdenPagoRelacion> CreditosUsados { get; set; } = [];
    /// <summary>Lo que quedó a favor del proveedor (anticipo) de esta orden.</summary>
    public decimal SaldoAFavor { get; set; }
    /// <summary>Dónde se usó el saldo a favor de esta orden después de grabarla.</summary>
    public List<OrdenPagoRelacion> UsosDelSaldo { get; set; } = [];
    public List<OrdenPagoAsientoLinea> Asiento { get; set; } = [];
    public List<OrdenPagoEvento> Historial { get; set; } = [];
}
