using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Domain.Entities;

public sealed class ClassifiedMovement
{
    public long ClassificationId { get; set; }
    public long MovementId { get; set; }
    public MovementCategory Category { get; set; }
    public decimal Confidence { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public string? ProbableClientCode { get; set; }
    public string? ProbableClientName { get; set; }
}
