namespace AlfaCore.Models;

public static class AuditEntityTypes
{
    public const string Contacto = "Contacto";
    public const string Usuario = "Usuario";
    public const string Tecnico = "Tecnico";
    public const string Configuracion = "Configuracion";
    public const string Ticket = "Ticket";
}

public static class AuditOperations
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Deactivate = "Deactivate";
    public const string Reactivate = "Reactivate";
    public const string ConversationCreated = "ConversationCreated";
}

public sealed class AuditWriteRequest
{
    public string Module { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyList<AuditChangeWriteDto> Changes { get; init; } = [];
}

public sealed class AuditChangeWriteDto
{
    public string FieldName { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public short Order { get; init; }
}

public sealed class AuditActivityPageDto
{
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public IReadOnlyList<AuditActivityEventDto> Items { get; init; } = [];
}

public sealed class AuditActivityEventDto
{
    public Guid Id { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string Message { get; init; } = string.Empty;
    public string MetadataJson { get; init; } = string.Empty;
    public IReadOnlyList<AuditActivityChangeDto> Changes { get; set; } = [];
}

public sealed class AuditActivityChangeDto
{
    public long Id { get; init; }
    public Guid AuditEventId { get; init; }
    public string FieldName { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public short Order { get; init; }
}

public sealed class AuditSchemaAvailabilityDto
{
    public bool Available { get; init; }
    public IReadOnlyList<string> MissingObjects { get; init; } = [];
}
