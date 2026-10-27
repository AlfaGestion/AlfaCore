namespace AlfaCore.ConciliacionBancaria.Domain.Entities;

/// <summary>
/// Detalle de una línea de aplicación de match, para mostrar en el panel
/// de detalles de la pantalla de revisión.
/// </summary>
public sealed class MatchDetailItem
{
    public long    MatchId         { get; set; }
    public string  MatchType       { get; set; } = string.Empty;
    public string  ClientCode      { get; set; } = string.Empty;
    public string  ClientName      { get; set; } = string.Empty;
    public decimal Confidence      { get; set; }
    public string  ProposalSummary { get; set; } = string.Empty;
    public string  Status          { get; set; } = string.Empty;

    // Línea de comprobante (puede ser null si el match es Anticipo/NoIdentificado)
    public string? DocumentType    { get; set; }
    public string? Letter          { get; set; }
    public int?    EmissionCenter  { get; set; }
    public long?   DocumentNumber  { get; set; }
    public int?    Installment     { get; set; }
    public decimal?AppliedAmount   { get; set; }
    public decimal?PendingBalance  { get; set; }
    public bool?   IsPartial       { get; set; }

    public string DocumentDisplay =>
        DocumentType is null
            ? "—"
            : $"{DocumentType} {Letter} {EmissionCenter:D4}-{DocumentNumber:D8} C{Installment}";
}
