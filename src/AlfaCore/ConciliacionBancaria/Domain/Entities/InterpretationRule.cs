using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Domain.Entities;

/// <summary>
/// Regla de interpretación almacenada en BK_ReglaInterpretacion.
/// Se evalúan en orden de Prioridad (menor número = mayor prioridad).
/// </summary>
public sealed class InterpretationRule
{
    public long RuleId { get; set; }

    /// <summary>Código único legible por humanos (ej. "MP_CREDITO").</summary>
    public string RuleCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Null = aplica a todos los orígenes.</summary>
    public string? SourceCode { get; set; }

    /// <summary>
    /// Texto a buscar (Contains, case-insensitive, normalizado).
    /// Null = no filtra por texto.
    /// </summary>
    public string? TextPattern { get; set; }

    /// <summary>
    /// Signo del importe: +1 = crédito, -1 = débito, null = cualquiera.
    /// </summary>
    public short? AmountSign { get; set; }

    public MovementCategory TargetCategory { get; set; }

    /// <summary>
    /// Si se especifica, se asigna como ClienteProbableCodigo en la clasificación.
    /// </summary>
    public string? FixedClientCode { get; set; }

    /// <summary>Menor número = mayor prioridad. Default 100.</summary>
    public int Priority { get; set; } = 100;
}
