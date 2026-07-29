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

public sealed class AlfaKnowledgeSuggestionResult
{
    public Guid InteractionId { get; set; }
    public string SuggestedReply { get; set; } = string.Empty;
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
    public bool HasSufficientContext { get; set; }
    public List<AlfaKnowledgeSuggestionCitation> Citations { get; set; } = [];
}
