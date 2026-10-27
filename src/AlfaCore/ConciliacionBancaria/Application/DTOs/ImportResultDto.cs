namespace AlfaCore.ConciliacionBancaria.Application.DTOs;

public sealed class ImportResultDto
{
    public long ImportId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public int ImportedRows { get; init; }
    public decimal TotalAmount { get; init; }
    public string Summary { get; init; } = string.Empty;
}
