namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

public sealed class ImportListItemDto
{
    public long ImportId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DateTime ImportedAtUtc { get; init; }
    public string ImportedBy { get; init; } = string.Empty;
    public int ImportedRows { get; init; }
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
}
