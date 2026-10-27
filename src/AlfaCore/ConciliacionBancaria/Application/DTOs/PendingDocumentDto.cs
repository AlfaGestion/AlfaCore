namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

/// <summary>
/// Comprobante pendiente de cobro leído desde VE_CPTES_SALDOS_VENTAS.
/// No se modifica la vista ni su lógica; solo se consulta.
/// </summary>
public sealed class PendingDocumentDto
{
    public string ClientCode     { get; init; } = string.Empty;
    public string ClientName     { get; init; } = string.Empty;

    /// <summary>Tipo de comprobante (ej. FAC, NCC, REC).</summary>
    public string DocumentType   { get; init; } = string.Empty;

    /// <summary>Letra del comprobante (A, B, C, M, etc.).</summary>
    public string Letter         { get; init; } = string.Empty;

    public int    EmissionCenter { get; init; }
    public long   DocumentNumber { get; init; }
    public int    Installment    { get; init; } = 1;

    /// <summary>Saldo pendiente según VE_CPTES_SALDOS_VENTAS. No recalculado.</summary>
    public decimal PendingBalance { get; init; }

    public DateTime? DueDate { get; init; }

    public string DisplayKey =>
        $"{DocumentType} {Letter} {EmissionCenter:D4}-{DocumentNumber:D8} C{Installment}";
}
