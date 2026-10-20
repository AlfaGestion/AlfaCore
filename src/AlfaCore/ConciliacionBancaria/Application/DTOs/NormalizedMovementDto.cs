using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

public sealed class NormalizedMovementDto
{
    public BankSource Source { get; init; }
    public DateTime MovementDate { get; init; }
    public DateTime? ValueDate { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal? Balance { get; init; }
    public string ExternalReference { get; init; } = string.Empty;
    public string CounterpartyName { get; init; } = string.Empty;
    public string CounterpartyTaxId { get; init; } = string.Empty;
    public string RawDataJson { get; init; } = "{}";
}
