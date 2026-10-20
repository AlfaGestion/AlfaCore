namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

/// <summary>
/// Movimiento clasificado como Cobranza con datos suficientes para el motor de matching.
/// Combina BK_ExtractoMovimiento + BK_MovimientoClasificado en una sola consulta.
/// </summary>
public sealed class CobranzaCandidateDto
{
    public long     MovementId             { get; init; }
    public DateTime MovementDate           { get; init; }
    public decimal  Amount                 { get; init; }
    public string   Description            { get; init; } = string.Empty;
    public string   CounterpartyName       { get; init; } = string.Empty;
    public string   CounterpartyTaxId      { get; init; } = string.Empty;

    /// <summary>
    /// Código de cliente sugerido por la clasificación (puede ser null si la regla
    /// no tenía ClienteFijoCodigo o si el movimiento quedó sin cliente probable).
    /// </summary>
    public string?  ClassifiedClientCode   { get; init; }
    public string? ClassifiedClientName { get; init; }
    public bool ClientManuallyAssigned { get; init; }
    public decimal  ClassificationConfidence { get; init; }
}
