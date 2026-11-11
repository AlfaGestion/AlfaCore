namespace AlfaCore.Models;

/// <summary>Solapas del reporte de stock (/stock/reportes), port de V_Cons_Ficha_Stock.</summary>
public enum StockReporteTipo { Saldos, Periodo, Ficha, Perecederos }

/// <summary>Filtros comunes. Cada solapa usa los que le corresponden.</summary>
public sealed class StockReporteFiltro
{
    public string Deposito { get; set; } = "";
    public string Rubro { get; set; } = "";
    public string Familia { get; set; } = "";
    /// <summary>Código, descripción o código de barras (Saldos, Período, Perecederos).</summary>
    public string Texto { get; set; } = "";
    /// <summary>Artículo de la ficha (código exacto).</summary>
    public string Articulo { get; set; } = "";
    public DateTime? Desde { get; set; }
    /// <summary>Saldos: saldo a esta fecha (vacío = hoy). Período/Ficha: fin del período.</summary>
    public DateTime? Hasta { get; set; }
    /// <summary>Saldos: oculta los saldos en cero. Período: solo artículos con movimientos.</summary>
    public bool SoloConStock { get; set; } = true;
    /// <summary>Saldos: un renglón por artículo con una columna por depósito.</summary>
    public bool DepositosEnColumnas { get; set; }
    /// <summary>Perecederos: "" (todos), VENCIDO, PORVENCER, VIGENTE, SINVTO.</summary>
    public string Estado { get; set; } = "";
    public int DiasPorVencer { get; set; } = 30;
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 50;
}

public sealed class StockReporteOpciones
{
    public List<CargaComprobantesOpcionFiltro> Depositos { get; set; } = [];
    public List<CargaComprobantesOpcionFiltro> Rubros { get; set; } = [];
    public List<CargaComprobantesOpcionFiltro> Familias { get; set; } = [];
    /// <summary>UNIDADDEMEDIDA_BL configurada: se muestran las columnas de bultos.</summary>
    public string UnidadBultos { get; set; } = "";
    public bool UsaPerecederos { get; set; }
}

public sealed class StockReportePagina<T>
{
    public List<T> Filas { get; set; } = [];
    public int Total { get; set; }
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 50;
    public decimal TotalUnidades { get; set; }
    public decimal TotalBultos { get; set; }
    /// <summary>Depósitos de las columnas (Saldos con depósitos en columnas).</summary>
    public List<string> Depositos { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
}

public sealed class StockSaldoFila
{
    public string Articulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Unidad { get; set; } = "";
    public string Rubro { get; set; } = "";
    public string Deposito { get; set; } = "";
    public decimal Saldo { get; set; }
    /// <summary>En bultos con la equivalencia del artículo; si no tiene, la suma de bultos de los movimientos. Null si no aplica.</summary>
    public decimal? Bultos { get; set; }
    /// <summary>Depósitos en columnas: saldo por depósito.</summary>
    public Dictionary<string, decimal> PorDeposito { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StockPeriodoFila
{
    public string Articulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Unidad { get; set; } = "";
    public string Deposito { get; set; } = "";
    public decimal SaldoInicial { get; set; }
    public decimal Entradas { get; set; }
    public decimal Salidas { get; set; }
    public decimal SaldoFinal { get; set; }
    public decimal? BultosFinal { get; set; }
}

public sealed class StockFicha
{
    public string Articulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Unidad { get; set; } = "";
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public decimal SaldoInicial { get; set; }
    public decimal SaldoFinal { get; set; }
    public decimal Entradas { get; set; }
    public decimal Salidas { get; set; }
    public List<StockFichaMovimiento> Movimientos { get; set; } = [];
    /// <summary>Saldo por depósito a la fecha final (la grilla de abajo de la ficha legacy).</summary>
    public List<StockSaldoFila> SaldosDeposito { get; set; } = [];
    public bool Truncada { get; set; }
}

public sealed class StockFichaMovimiento
{
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public string Deposito { get; set; } = "";
    public string OrigenDestino { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string Cuenta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal Entrada { get; set; }
    public decimal Salida { get; set; }
    public decimal Saldo { get; set; }
    public decimal Bultos { get; set; }
    public string Lote { get; set; } = "";
    public DateTime? Vencimiento { get; set; }
    public string Observaciones { get; set; } = "";
}

public sealed class StockPerecederoFila
{
    public string Articulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Unidad { get; set; } = "";
    public string Rubro { get; set; } = "";
    public string Deposito { get; set; } = "";
    public DateTime? Ingreso { get; set; }
    public DateTime? Vencimiento { get; set; }
    public decimal Cantidad { get; set; }
    public decimal? Bultos { get; set; }
    public int? DiasRestantes { get; set; }
    /// <summary>VENCIDO, PORVENCER, VIGENTE o SINVTO.</summary>
    public string Estado { get; set; } = "";
    /// <summary>El vencimiento salió de la fecha de ingreso + días del artículo (no estaba registrado).</summary>
    public bool Estimado { get; set; }
}
