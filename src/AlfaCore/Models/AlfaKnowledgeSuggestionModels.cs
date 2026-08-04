namespace AlfaCore.Models;

public sealed class AlfaKnowledgeSuggestionCitation
{
    public int CitationNumber { get; set; }
    public string ChunkKey { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
}

public sealed class AlfaKnowledgeAssistantMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class AlfaKnowledgeImageInput
{
    public string DataUrl { get; set; } = string.Empty;
    public string? FileName { get; set; }
}

public sealed class AlfaKnowledgeSuggestionResult
{
    public Guid InteractionId { get; set; }
    public string SuggestedReply { get; set; } = string.Empty;
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
    public bool HasSufficientContext { get; set; }
    public bool ImageAnalyzed { get; set; }
    public string? ImageContext { get; set; }
    public List<AlfaKnowledgeSuggestionCitation> Citations { get; set; } = [];
}

public sealed class AlfaKnowledgeCorrectionRequest
{
    public Guid? InteractionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalQuestion { get; set; } = string.Empty;
    public string? OriginalAnswer { get; set; }
    public string CorrectedAnswer { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
    public string? CorrectionNotes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
