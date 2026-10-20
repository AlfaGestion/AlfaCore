using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Domain.Entities;

public sealed class MovementMatchProposal
{
    public long MatchId { get; set; }
    public long MovementId { get; set; }
    public MatchingType MatchingType { get; set; }
    public decimal Confidence { get; set; }
    public string ClientCode { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ProposalSummary { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string Status { get; set; } = "PENDING";
}
