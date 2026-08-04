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
