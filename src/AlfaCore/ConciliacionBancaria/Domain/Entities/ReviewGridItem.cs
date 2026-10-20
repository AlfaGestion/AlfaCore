using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Domain.Entities;

public sealed class ReviewGridItem
{
    public long MovementId { get; set; }
    public DateTime Date { get; set; }
    public string OriginalDescription { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public MovementCategory Category { get; set; }
    public string ProbableClient { get; set; } = string.Empty;
    public string ProbableDocuments { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string Observations { get; set; } = string.Empty;
    public ReviewStatus ReviewStatus { get; set; }
}
