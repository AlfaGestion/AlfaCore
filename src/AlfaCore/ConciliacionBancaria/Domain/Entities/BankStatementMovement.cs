using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Domain.Entities;

public sealed class BankStatementMovement
{
    public long MovementId { get; set; }
    public long ImportId { get; set; }
    public BankSource Source { get; set; }
    public DateTime MovementDate { get; set; }
    public DateTime? ValueDate { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public string CurrencyCode { get; set; } = "ARS";
    public string CounterpartyTaxId { get; set; } = string.Empty;
    public string CounterpartyName { get; set; } = string.Empty;
    public string RawDataJson { get; set; } = "{}";
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pendiente;
}
