namespace AlfaCore.Models;

/// <summary>Filtro común de los reportes contables (como cn000r del legacy): ejercicio, rango de fechas
/// dentro del ejercicio, unidades de negocio y proformas.</summary>
public sealed class ContabilidadFiltro
{
    public int Periodo { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    /// <summary>Vacío = todas las unidades.</summary>
    public List<string> Unidades { get; set; } = [];
    public bool IncluirProformas { get; set; }
    // Mayor
    public string CuentaDesde { get; set; } = string.Empty;
    public string CuentaHasta { get; set; } = string.Empty;
    // Sumas y saldos
    /// <summary>Clientes y proveedores (subcuentas CL/PR) sumados en su cuenta título.</summary>
    public bool AgruparTerceros { get; set; } = true;
    public bool MostrarTitulos { get; set; } = true;
    public bool IncluirSinMovimientos { get; set; }
}

public sealed record ContabilidadEjercicio(int Periodo, string Descripcion, DateTime Desde, DateTime Hasta, bool Cerrado);
public sealed record ContabilidadOpcion(string Codigo, string Descripcion);

public sealed class ContabilidadCatalogos
{
    public IReadOnlyList<ContabilidadEjercicio> Ejercicios { get; set; } = [];
    public IReadOnlyList<ContabilidadOpcion> Unidades { get; set; } = [];
    public bool PuedeVerProformas { get; set; }
}

// ── Balance de sumas y saldos ────────────────────────────────────────────────────────────────────

public sealed class SumasSaldosFila
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool EsTitulo { get; set; }
    public int Nivel { get; set; }
    public decimal SaldoAnterior { get; set; }
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    public decimal Saldo => SaldoAnterior + Debe - Haber;
    /// <summary>Cantidad de subcuentas sumadas (clientes/proveedores agrupados).</summary>
    public int Agrupadas { get; set; }
}

public sealed record AsientoDesbalanceado(int MesOperativo, int Numero, DateTime Fecha, string Tc, string Comprobante, string Detalle, int Renglones, decimal Diferencia);

public sealed class SumasSaldosResultado
{
    public ContabilidadFiltro Filtro { get; set; } = new();
    public string Empresa { get; set; } = string.Empty;
    public string Ejercicio { get; set; } = string.Empty;
    public IReadOnlyList<SumasSaldosFila> Filas { get; set; } = [];
    public decimal TotalSaldoAnterior { get; set; }
    public decimal TotalDebe { get; set; }
    public decimal TotalHaber { get; set; }
    public decimal TotalSaldo => TotalSaldoAnterior + TotalDebe - TotalHaber;
    public IReadOnlyList<AsientoDesbalanceado> Desbalanceados { get; set; } = [];
}

// ── Mayor ────────────────────────────────────────────────────────────────────────────────────────

public sealed class MayorMovimiento
{
    public DateTime Fecha { get; set; }
    public int MesOperativo { get; set; }
    public int Asiento { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    public decimal Saldo { get; set; }
}

public sealed class MayorCuenta
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal SaldoAnterior { get; set; }
    public List<MayorMovimiento> Movimientos { get; set; } = [];
    public decimal Debe => Movimientos.Sum(m => m.Debe);
    public decimal Haber => Movimientos.Sum(m => m.Haber);
    public decimal Saldo => SaldoAnterior + Debe - Haber;
}

public sealed class MayorResultado
{
    public ContabilidadFiltro Filtro { get; set; } = new();
    public string Empresa { get; set; } = string.Empty;
    public string Ejercicio { get; set; } = string.Empty;
    public IReadOnlyList<MayorCuenta> Cuentas { get; set; } = [];
    public bool Truncado { get; set; }
    public int Movimientos => Cuentas.Sum(c => c.Movimientos.Count);
}

public sealed record ContabilidadArchivo(string Nombre, string ContentType, byte[] Contenido);
