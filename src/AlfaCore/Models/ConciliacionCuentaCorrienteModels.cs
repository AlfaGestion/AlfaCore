namespace AlfaCore.Models;

public sealed record ConciliacionAsientoClave(string Periodo, byte Mes, int Asiento, string TipoRegistro, int Secuencia);

public sealed class ConciliacionCtaMovimiento
{
    public decimal ImporteBanco { get; set; }
    public bool ReciboCercano { get; set; }
    public string Periodo { get; set; } = "";
    public byte Mes { get; set; }
    public int Asiento { get; set; }
    public string TipoRegistro { get; set; } = "";
    public int Secuencia { get; set; }
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public string Detalle { get; set; } = "";
    public string Moneda { get; set; } = "";
    public string CodigoMoneda { get; set; } = "";
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    public decimal Saldo { get; set; }
    public string Usuario { get; set; } = "";
    public string Caja { get; set; } = "";
    public string Unidad { get; set; } = "";
    public bool PosibleCoincidencia { get; set; }
    public ConciliacionAsientoClave Clave => new(Periodo, Mes, Asiento, TipoRegistro, Secuencia);
}

public sealed class ConciliacionCtaSaldo
{
    public string Moneda { get; set; } = "";
    public decimal Saldo { get; set; }
}

public sealed record ConciliacionCuentaCorriente(IReadOnlyList<ConciliacionCtaMovimiento> Movimientos,
    IReadOnlyList<ConciliacionCtaMovimiento> Coincidencias, IReadOnlyList<ConciliacionCtaSaldo> Saldos);

public sealed record ConciliacionSaldoResumen(long MovimientoId, DateTime FechaCorte,
    IReadOnlyList<ConciliacionCtaSaldo> Saldos, bool ConsultaFallida = false);

public sealed class ConciliacionContrapartida
{
    public string Cuenta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Medio { get; set; } = "";
    public string Signo { get; set; } = "";
    public decimal Importe { get; set; }
    public string Moneda { get; set; } = "";
    public string Referencia { get; set; } = "";
}

public sealed class ConciliacionCtaAplicacion
{
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public decimal Importe { get; set; }
    public DateTime? Fecha { get; set; }
    public string Comentarios { get; set; } = "";
}

public sealed record ConciliacionAsientoDetalle(ConciliacionCtaMovimiento Movimiento, string Comentarios,
    IReadOnlyList<ConciliacionContrapartida> Contrapartidas, IReadOnlyList<ConciliacionCtaAplicacion> Aplicaciones);
