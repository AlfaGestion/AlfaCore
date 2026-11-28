namespace AlfaCore.Models;

public sealed class CostosProfileDto
{
    public int Id { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string ProviderAccount { get; init; } = string.Empty;
    public string PricePolicy { get; init; } = string.Empty;
    public string ListCode { get; init; } = string.Empty;
    public string SheetName { get; init; } = string.Empty;
    public string RangeFrom { get; init; } = string.Empty;
    public string RangeTo { get; init; } = string.Empty;
    public string KeyFields { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool OnlyAdd { get; init; }
    public bool OnlyModify { get; init; }
}

public sealed class CostosProviderLookupDto
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public sealed class CostosImportedRowDto
{
    public int RowNumber { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal? CostPrice { get; init; }
    public string RawPrice { get; init; } = string.Empty;
    public string? SourceSheet { get; init; }
    public Dictionary<string, string> RawValues { get; init; } = [];
}

public sealed class CostosBatchSummaryDto
{
    public int Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string ProviderAccount { get; init; } = string.Empty;
    public string SourceFileName { get; init; } = string.Empty;
    public string SourceFilePath { get; init; } = string.Empty;
    public string SourceKind { get; init; } = string.Empty;
    public int TotalRowsRead { get; init; }
    public int TotalRowsWithCost { get; init; }
    public int TotalConfirmed { get; init; }
    public int TotalUpdated { get; init; }
    public int TotalErrors { get; init; }
}

public sealed class CostosBatchDetailRowDto
{
    public int DetailId { get; init; }
    public int RowNumber { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ProviderCodeRead { get; init; } = string.Empty;
    public string DescriptionRead { get; init; } = string.Empty;
    public decimal? CostRead { get; init; }
    public string ArticleProviderCode { get; init; } = string.Empty;
    public string ArticleId { get; init; } = string.Empty;
    public string ArticleDescription { get; init; } = string.Empty;
    public decimal? CurrentCost { get; init; }
    public decimal? NewCost { get; init; }
    public string MatchType { get; init; } = string.Empty;
    public double MatchScore { get; init; }
    public double? VariationPct { get; init; }
    public string AlertDetail { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string ApplyResult { get; init; } = string.Empty;
    public string ApplyError { get; init; } = string.Empty;

    public bool HasChosenArticle => !string.IsNullOrWhiteSpace(ArticleId);
    public bool CanBeChecked => HasChosenArticle && !string.Equals(Status, "DESCARTADO", StringComparison.OrdinalIgnoreCase);
}

public sealed class CostosBatchDetailDto
{
    public CostosBatchSummaryDto Batch { get; init; } = new();
    public CostosProfileDto? Profile { get; init; }
    public IReadOnlyList<CostosBatchDetailRowDto> Rows { get; init; } = [];
}

public sealed class CostosMatchCandidateDto
{
    public int ImportedRowNumber { get; init; }
    public string ArticleId { get; init; } = string.Empty;
    public string ArticleCode { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal? CurrentCost { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string ProviderAccount { get; init; } = string.Empty;
    public string MatchType { get; init; } = string.Empty;
    public double Score { get; init; }
    public bool ProviderCodeHit { get; init; }
    public double DescriptionScore { get; init; }
    public double? VariationPct { get; init; }
    public string SearchScope { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed class CostosHistoryDto
{
    public int? ImportBatchId { get; init; }
    public DateTime Timestamp { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int RowNumber { get; init; }
    public string ArticleId { get; init; } = string.Empty;
    public string ImportedDescription { get; init; } = string.Empty;
    public decimal? PreviousCost { get; init; }
    public decimal? NewCost { get; init; }
    public string MatchType { get; init; } = string.Empty;
    public double MatchScore { get; init; }
    public string AlertText { get; init; } = string.Empty;
}

public sealed class CostosImportResultDto
{
    public CostosBatchSummaryDto Batch { get; init; } = new();
    public IReadOnlyList<CostosImportedRowDto> Rows { get; init; } = [];
}

public sealed class CostosApplyResultDto
{
    public int BatchId { get; init; }
    public int Updated { get; init; }
    public int Same { get; init; }
    public int Errors { get; init; }
    public string Status { get; init; } = string.Empty;
    /// <summary>Productos recalculados en cascada por usar como insumo a los artículos actualizados (fórmulas).</summary>
    public int ProductosRecalculados { get; init; }
    public bool PreciosAlBorrador { get; init; }
}

public sealed class CostosUndoResultDto
{
    public int BatchId { get; init; }
    public int Reverted { get; init; }
    public int Errors { get; init; }
    public string Status { get; init; } = string.Empty;
    /// <summary>Productos recalculados en cascada por usar como insumo a los artículos revertidos (fórmulas).</summary>
    public int ProductosRecalculados { get; init; }
    public bool PreciosAlBorrador { get; init; }
}

public sealed class CostosColumnDto
{
    public int ColIndex { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class CostosFileColumnsDto
{
    public bool Detected { get; init; }
    public IReadOnlyList<CostosColumnDto> AllColumns { get; init; } = [];
    public IReadOnlyList<CostosColumnDto> PriceCandidates { get; init; } = [];
    public int DefaultPriceColIndex { get; init; } = -1;
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; init; } = [];
}
