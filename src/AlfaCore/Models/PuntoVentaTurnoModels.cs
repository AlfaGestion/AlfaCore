namespace AlfaCore.Models;

public static class PuntoVentaTurnoEstadoKeys
{
    public const string Abierto = "ABIERTO";
    public const string Cerrado = "CERRADO";
}

public static class PuntoVentaTurnoMomentoKeys
{
    public const string Apertura = "APERTURA";
    public const string Cierre = "CIERRE";
}

public sealed class PuntoVentaDenominacionDto
{
    public int Id { get; set; }
    public decimal Valor { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public int Orden { get; set; }
}

public sealed class PuntoVentaTurnoBilleteDto
{
    public int IdDenominacion { get; set; }
    public decimal Valor { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public decimal Subtotal => Valor * Cantidad;
}

public sealed class PuntoVentaTurnoDto
{
    public int Id { get; set; }
    public int IdPuntoVenta { get; set; }
    public string IdCaja { get; set; } = string.Empty;
    public DateTime FechaOperativa { get; set; }
    public DateTime FechaHoraApertura { get; set; }
    public DateTime? FechaHoraCierre { get; set; }
    public string UsuarioApertura { get; set; } = string.Empty;
    public string UsuarioCierre { get; set; } = string.Empty;
    public decimal SaldoInicial { get; set; }
    public decimal? SaldoFinalDeclarado { get; set; }
    public decimal? SaldoFinalTeorico { get; set; }
    public decimal? Diferencia { get; set; }
    public string Estado { get; set; } = PuntoVentaTurnoEstadoKeys.Abierto;
    public string Observaciones { get; set; } = string.Empty;
    public List<PuntoVentaTurnoBilleteDto> BilletesApertura { get; set; } = [];
    public List<PuntoVentaTurnoBilleteDto> BilletesCierre { get; set; } = [];
}

public sealed class PuntoVentaTurnoBilleteDetalleRequest
{
    public int IdDenominacion { get; set; }
    public int Cantidad { get; set; }
}

public sealed class PuntoVentaTurnoAperturaRequest
{
    public int IdPuntoVenta { get; set; }
    public string IdCaja { get; set; } = string.Empty;
    public decimal SaldoInicial { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
    public List<PuntoVentaTurnoBilleteDetalleRequest> Billetes { get; set; } = [];
}

public sealed class PuntoVentaTurnoCierreRequest
{
    public int IdTurno { get; set; }
    public decimal SaldoFinalDeclarado { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
    public List<PuntoVentaTurnoBilleteDetalleRequest> Billetes { get; set; } = [];

    /// <summary>Efectivo que queda en la caja para el turno siguiente (la apertura lo propone).</summary>
    public decimal? FondoQueda { get; set; }

    /// <summary>Caja destino de la transferencia (vacia = no se transfiere).</summary>
    public string? IdCajaDestino { get; set; }

    public List<PuntoVentaTurnoTransferenciaLineaRequest> Transferencia { get; set; } = [];
}

public sealed class PuntoVentaTurnoTransferenciaLineaRequest
{
    public string Cuenta { get; set; } = string.Empty;
    public string Moneda { get; set; } = string.Empty;
    public decimal Cotizacion { get; set; } = 1;
    public decimal Importe { get; set; }
}

/// <summary>Saldo de un medio de pago (cuenta con MedioDePago CH/DB/EF/TJ) en la caja del turno.</summary>
public sealed class PuntoVentaTurnoSaldoMedioDto
{
    public string Cuenta { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string MedioDePago { get; set; } = string.Empty;
    public string Moneda { get; set; } = string.Empty;
    public decimal Cotizacion { get; set; } = 1;
    public decimal Saldo { get; set; }
    public bool EsEfectivoLocal { get; set; }
}

public sealed class PuntoVentaCajaOpcionDto
{
    public string IdCaja { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

/// <summary>Datos para armar el cierre: saldos por medio de pago y caja destino propuesta (TPV_CAJADESTINO).</summary>
public sealed class PuntoVentaTurnoCierrePreviewDto
{
    public decimal SaldoTeorico { get; set; }
    public string IdCajaDestinoSugerida { get; set; } = string.Empty;
    public List<PuntoVentaCajaOpcionDto> Cajas { get; set; } = [];
    public List<PuntoVentaTurnoSaldoMedioDto> Saldos { get; set; } = [];
}

public sealed class PuntoVentaTurnoCierreResultDto
{
    public string Comprobante { get; set; } = string.Empty;
    public string CajaDestino { get; set; } = string.Empty;
    public decimal ImporteTransferido { get; set; }
    public decimal Diferencia { get; set; }
}

/// <summary>Corte X: arqueo parcial que NO cierra el turno. Sirve para relevos de cajero o controles durante el día.</summary>
public sealed class PuntoVentaTurnoCorteXRequest
{
    public int IdTurno { get; set; }
    public decimal? SaldoContado { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
    public List<PuntoVentaTurnoBilleteDetalleRequest> Billetes { get; set; } = [];
}

public sealed class PuntoVentaTurnoCorteXDto
{
    public int Id { get; set; }
    public int IdTurno { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public decimal SaldoTeorico { get; set; }
    public decimal? SaldoContado { get; set; }
    public decimal? Diferencia { get; set; }
    public string Observaciones { get; set; } = string.Empty;
}

/// <summary>Fila de resumen para la vista "saldos por caja" de un punto de venta.</summary>
public sealed class PuntoVentaCajaSaldoDto
{
    public int IdTurno { get; set; }
    public string IdCaja { get; set; } = string.Empty;
    public string UsuarioApertura { get; set; } = string.Empty;
    public DateTime FechaHoraApertura { get; set; }
    public decimal SaldoInicial { get; set; }
    public decimal SaldoTeoricoActual { get; set; }
}
