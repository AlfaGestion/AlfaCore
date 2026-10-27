namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

/// <summary>
/// Línea de aplicación de un match: un comprobante pendiente al que se imputa
/// todo o parte del importe del movimiento bancario.
/// Se guarda en BK_MovimientoAplicacion.
/// </summary>
public sealed class MovementApplicationDto
{
    /// <summary>Se asigna después de insertar el registro padre en BK_MovimientoMatch.</summary>
    public long MatchId { get; set; }

    public string  DocumentType    { get; init; } = string.Empty;
    public string  Letter          { get; init; } = string.Empty;
    public int     EmissionCenter  { get; init; }
    public long    DocumentNumber  { get; init; }
    public int     Installment     { get; init; } = 1;

    /// <summary>Importe aplicado de este movimiento a este comprobante.</summary>
    public decimal AppliedAmount   { get; init; }

    /// <summary>Saldo del comprobante antes de esta aplicación (según VE_CPTES_SALDOS_VENTAS).</summary>
    public decimal PendingBalance  { get; init; }

    /// <summary>True si el importe aplicado es menor al saldo del comprobante.</summary>
    public bool    IsPartial       { get; init; }
}
