namespace AlfaCore.Models;

/// <summary>Datos de la pantalla Movimientos de stock (/stock/movimientos), como V_MV_STOCK del legacy.</summary>
public sealed class StockMovimientosContexto
{
    public string Usuario { get; set; } = "";
    public string UnidadNegocio { get; set; } = "";
    public bool AdminCentral { get; set; }
    public string MotivoDefault { get; set; } = "";
    /// <summary>NoFacturaSinStock=SI: el trigger de V_MV_STOCK rechaza salidas sin stock.</summary>
    public bool ValidaStockSalidas { get; set; }
    public DateTime? CierreStock { get; set; }
    public DateTime? OperativaDesde { get; set; }
    public DateTime? OperativaHasta { get; set; }
    public List<StockMovimientoTipo> Tipos { get; set; } = [];
    public List<CargaComprobantesOpcionFiltro> UnidadesNegocio { get; set; } = [];
    public List<StockMovimientoDeposito> Depositos { get; set; } = [];
    public List<CargaComprobantesOpcionFiltro> Motivos { get; set; } = [];
}

/// <summary>Comprobante de V_TA_CPTE con SISTEMA='Stock' habilitado para la unidad de negocio.</summary>
public sealed class StockMovimientoTipo
{
    public string Tc { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string UnidadNegocio { get; set; } = "";
    public string Es { get; set; } = "";
    public string IdDeposito { get; set; } = "";
}

public sealed class StockMovimientoDeposito
{
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string UnidadNegocio { get; set; } = "";
    public bool Compartido { get; set; }
    /// <summary>V_TA_DEPOSITO.Empresa: depósito de otra base (transferencia remota, no soportada todavía).</summary>
    public string Empresa { get; set; } = "";
}

/// <summary>Artículo para la grilla: unidad de stock, costo y saldo en el depósito.</summary>
public sealed class StockMovimientoArticulo
{
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Unidad { get; set; } = "";
    public decimal Costo { get; set; }
    public decimal Stock { get; set; }
    /// <summary>Motivo por el que todavía no se puede mover en esta pantalla (lotes, series, talle...); vacío si se puede.</summary>
    public string Restriccion { get; set; } = "";
}

/// <summary>Carga de la pantalla. En AJP/SI la cantidad es la contada (stock físico), no la diferencia.</summary>
public sealed class StockMovimientoBorrador
{
    public string Tc { get; set; } = "";
    public string UnidadNegocio { get; set; } = "";
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Deposito { get; set; } = "";
    public string DepositoDestino { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string Observaciones { get; set; } = "";
    /// <summary>Comprobante que se modifica (SSSSNNNNNNNNL); vacío en uno nuevo.</summary>
    public string Comprobante { get; set; } = "";
    public List<StockMovimientoLinea> Lineas { get; set; } = [];
}

public sealed class StockMovimientoLinea
{
    public string Articulo { get; set; } = "";
    /// <summary>Cantidad en la unidad del artículo (IDUNIDAD). Si se cargan bultos se calcula desde ellos.</summary>
    public decimal Cantidad { get; set; }
    public decimal? Bultos { get; set; }
    /// <summary>AJP/SI: saldo que se mostró al calcular; si al grabar es otro, se pide recalcular.</summary>
    public decimal? SaldoEsperado { get; set; }
}

/// <summary>Resultado de calcular la carga: lo que se va a grabar por artículo.</summary>
public sealed class StockMovimientoPlan
{
    public string Tc { get; set; } = "";
    public DateTime Fecha { get; set; }
    public List<StockMovimientoPlanLinea> Lineas { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
}

public sealed class StockMovimientoPlanLinea
{
    public string Articulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Unidad { get; set; } = "";
    /// <summary>Cantidad cargada en unidades (la contada en AJP/SI).</summary>
    public decimal Cantidad { get; set; }
    public decimal Bultos { get; set; }
    /// <summary>AJP/SI: saldo del depósito a la fecha (movimientos hasta ese día inclusive, sin este comprobante). Resto: stock actual del depósito origen.</summary>
    public decimal Saldo { get; set; }
    /// <summary>Lo que se graba con signo en el depósito (origen en TR): diferencia en AJP/SI, ± cantidad en el resto.</summary>
    public decimal Movimiento { get; set; }
}

public sealed class StockMovimientoResultado
{
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public string Accion { get; set; } = "";
    public int Lineas { get; set; }
}

/// <summary>Fila del buscador de comprobantes para modificar o anular.</summary>
public sealed class StockMovimientoResumen
{
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string Deposito { get; set; } = "";
    public string DepositoDestino { get; set; } = "";
    public string UnidadNegocio { get; set; } = "";
    public int Items { get; set; }
    public string Observaciones { get; set; } = "";
}

/// <summary>Comprobante recuperado para modificar: la carga tal como se ingresó.</summary>
public sealed class StockMovimientoCargado
{
    public StockMovimientoBorrador Borrador { get; set; } = new();
    public Dictionary<string, StockMovimientoArticulo> Articulos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
