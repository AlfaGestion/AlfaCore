namespace AlfaCore.Models;

public sealed class AsientosFiltro
{
    /// <summary>Sin fechas («Todo»): los últimos asientos cargados, buscando hacia atrás desde hoy.</summary>
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    /// <summary>Número («10-25» o «25»), comprobante, detalle, cuenta o usuario.</summary>
    public string Texto { get; set; } = string.Empty;
    /// <summary>MANUALES (editables), COMPROBANTES (de compras, ventas, cobranzas, pagos…) o TODOS.</summary>
    public string Origen { get; set; } = "MANUALES";
    public string Cuenta { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public bool IncluirProformas { get; set; }
}

/// <summary>Filtros de la pantalla de Asientos para el buscador estándar (GestionSmartSearch). Por defecto, «Todo».</summary>
public sealed class AsientosBusqueda : GestionDateFilterBase
{
    public AsientosBusqueda()
    {
        FechaDesde = null;
        FechaHasta = null;
    }

    public string Origen { get; set; } = "TODOS";
    public string Cuenta { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public bool IncluirProformas { get; set; }
}

/// <summary>Clave de un asiento: PERIODO + MES_OPERATIVO + [NUMERO ASIENTO] + TIPO_REG.</summary>
public sealed record AsientoClave(string Periodo, int Mes, int Numero, string TipoReg)
{
    /// <summary>Como lo muestra el legacy: MM-0000000.</summary>
    public string Texto => $"{Mes:00}-{Numero:0000000}";
    public string Id => $"{Periodo.Trim()}|{Mes}|{Numero}|{TipoReg.Trim()}";

    public static AsientoClave? Parse(string? id)
    {
        var p = (id ?? "").Split('|');
        return p.Length == 4 && int.TryParse(p[1], out var m) && int.TryParse(p[2], out var n) && p[0].Trim().Length > 0 ? new(p[0].Trim(), m, n, p[3]) : null;
    }
}

public sealed class AsientoResumen
{
    public string Periodo { get; set; } = string.Empty;
    public int Mes { get; set; }
    public int Numero { get; set; }
    public string TipoReg { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Renglones { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string LivaTipo { get; set; } = string.Empty;
    public bool Manual { get; set; }
    public AsientoClave Clave => new(Periodo, Mes, Numero, TipoReg);
}

public sealed class AsientoRenglon
{
    public string Cuenta { get; set; } = string.Empty;
    public string CuentaNombre { get; set; } = string.Empty;
    /// <summary>Concepto del renglón (opcional); vacío = el detalle del asiento.</summary>
    public string Detalle { get; set; } = string.Empty;
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    /// <summary>Moneda de la cuenta (TA_MONEDAS). Debe y Haber van siempre en pesos; en moneda extranjera se carga la cantidad y la cotización.</summary>
    public string Moneda { get; set; } = "1";
    public decimal Cotizacion { get; set; } = 1;
    public decimal ImporteMoneda { get; set; }
    /// <summary>Datos del cheque cuando la cuenta es de cheques (MEDIODEPAGO CH).</summary>
    public AsientoCheque? Cheque { get; set; }
    /// <summary>Distribución en centros de costo (MV_ASIENTOSCCOSTO); vacía = sin distribuir.</summary>
    public List<AsientoCentro> Centros { get; set; } = [];
    /// <summary>Vino de un modelo: si queda sin importe, se omite al grabar (como el legacy).</summary>
    public bool DeModelo { get; set; }
    /// <summary>El modelo no deja cambiar la cuenta de este renglón.</summary>
    public bool CuentaFija { get; set; }
    /// <summary>Modelos: lado de la cuenta cuando el renglón no tiene importe (MA_MODELOS.DEBE-HABER).</summary>
    public bool AlHaber { get; set; }
}

public sealed class AsientoCentro
{
    public string CCosto { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Porcentaje { get; set; }
    public decimal Importe { get; set; }
}

/// <summary>Distribución sugerida de una cuenta (MA_CUENTASCCOSTO o la plantilla MA_CUENTAS_CCDIST).</summary>
public sealed record AsientoCentroDefault(string Cuenta, string CCosto, decimal Porcentaje, bool PideConfirmacion);

public sealed class AsientoModelo
{
    public string Nombre { get; set; } = string.Empty;
    public string Tc { get; set; } = string.Empty;
    public bool ModificaTc { get; set; } = true;
    public bool ModificaCuentas { get; set; } = true;
    public bool AgregaCuentas { get; set; } = true;
    public List<AsientoRenglon> Renglones { get; set; } = [];
}

public sealed class AsientoDetalle
{
    public AsientoClave? Clave { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Tc { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = "X";
    public string Detalle { get; set; } = string.Empty;
    public DateTime? Vencimiento { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime? Grabado { get; set; }
    public DateTime? Modificado { get; set; }
    public List<AsientoRenglon> Renglones { get; set; } = [];
    /// <summary>
    /// Moneda en que se cargan los importes de todo el asiento ("1" = pesos). Como el legacy (CmbMoneda + ConvierteMoneda):
    /// si es otra, debe y haber de los renglones van en esa moneda y al grabar se pasan a pesos con la cotización del encabezado.
    /// </summary>
    public string MonedaAsiento { get; set; } = "1";
    public decimal CotizacionAsiento { get; set; } = 1m;
    public bool EnOtraMoneda => (MonedaAsiento ?? "").Trim() is { Length: > 0 } m && m != "1";
    public bool Editable { get; set; } = true;
    /// <summary>Por qué no se puede modificar desde acá (comprobante de compras, ventas, cobranzas, conciliado…).</summary>
    public string MotivoSoloLectura { get; set; } = string.Empty;
    /// <summary>Datos del comprobante cuando el asiento viene de una factura (libro de IVA).</summary>
    public string LivaTipo { get; set; } = string.Empty;
    public string CuentaComprobante { get; set; } = string.Empty;
    public string NombreComprobante { get; set; } = string.Empty;
    public string CuitComprobante { get; set; } = string.Empty;
    public decimal TotalComprobante { get; set; }
    public string Cae { get; set; } = string.Empty;
    public string Comprobante => $"{Tc} {Sucursal}-{Numero} {Letra}".Trim();
    /// <summary>Huella de lo leído: al grabar o anular se verifica que nadie lo haya cambiado mientras tanto.</summary>
    public string Huella { get; set; } = string.Empty;
    public decimal TotalDebe => Renglones.Sum(r => r.Debe);
    public decimal TotalHaber => Renglones.Sum(r => r.Haber);
}

public sealed record AsientoCuenta(string Codigo, string Descripcion, string MedioDePago, bool PideVencimiento, string Moneda = "1", string TipoVista = "")
{
    /// <summary>Cuenta de cheques (MEDIODEPAGO CH): cada renglón es un cheque.</summary>
    public bool EsCheque => MedioDePago.Equals("CH", StringComparison.OrdinalIgnoreCase);
    /// <summary>Cuenta bancaria propia (TIPOVISTA BC): sus cheques son propios (salen de una chequera).</summary>
    public bool Bancaria => TipoVista.Equals("BC", StringComparison.OrdinalIgnoreCase);
    public bool EnMonedaExtranjera => Moneda.Trim() is not ("" or "1");
}

public sealed record AsientoMoneda(string Codigo, string Descripcion);

/// <summary>Datos del cheque de un renglón (MV_ASIENTOS: NroComprobanteBancario = «Serie-Número», IdBanco, Clearing, VENCIMIENTO, TJ_OBSERVACIONES = CUIT del librador).</summary>
public sealed class AsientoCheque
{
    public string Serie { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string IdBanco { get; set; } = string.Empty;
    public string Banco { get; set; } = string.Empty;
    public DateTime? Vencimiento { get; set; }
    public int Clearing { get; set; } = 48;
    public string Cuit { get; set; } = string.Empty;
    public string NroComprobante => $"{Serie.Trim()}-{Numero.Trim()}";
}

/// <summary>Cheque de terceros en cartera (V_ChequesEnCartera): entró y todavía no salió.</summary>
public sealed class ChequeCartera
{
    public string Cuenta { get; set; } = string.Empty;
    public string NroComprobante { get; set; } = string.Empty;
    public string IdBanco { get; set; } = string.Empty;
    public string Banco { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public DateTime? Vencimiento { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public int Clearing { get; set; }
    public DateTime Fecha { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}

public sealed record AsientoTalonario(string Codigo, string Descripcion, string Letras, int Sucursal, int Proximo, string Unidad);

public sealed class AsientosCatalogo
{
    public IReadOnlyList<AsientoCuenta> Cuentas { get; set; } = [];
    public IReadOnlyList<AsientoTalonario> Talonarios { get; set; } = [];
    public IReadOnlyList<CompraCargaOpcion> Unidades { get; set; } = [];
    public IReadOnlyList<CompraCargaOpcion> Centros { get; set; } = [];
    public IReadOnlyList<AsientoMoneda> Monedas { get; set; } = [];
    public IReadOnlyList<ChequeraBanco> Bancos { get; set; } = [];
    public IReadOnlyList<AsientoCentroDefault> CentrosPorCuenta { get; set; } = [];
    public IReadOnlyList<string> Modelos { get; set; } = [];
    public bool PuedeGuardarModelos { get; set; }
    public bool PuedeVerProformas { get; set; }
    public string UnidadUsuario { get; set; } = string.Empty;
    public bool PuedeCargar { get; set; }
    public bool PuedeModificar { get; set; }
    public bool PuedeAnular { get; set; }
}

/// <summary>Cruce del asiento de una factura con Mis Comprobantes de ARCA (último archivo bajado del mes).</summary>
public sealed class AsientoControlArca
{
    /// <summary>COINCIDE, DIFIERE, NO_INFORMADO, SIN_ARCHIVO o NO_APLICA.</summary>
    public string Estado { get; set; } = "NO_APLICA";
    public string Mensaje { get; set; } = string.Empty;
    public string Archivo { get; set; } = string.Empty;
    public decimal? TotalArca { get; set; }
    public string CaeArca { get; set; } = string.Empty;
    public DateTime? FechaArca { get; set; }
    public IReadOnlyList<string> Observaciones { get; set; } = [];
}
