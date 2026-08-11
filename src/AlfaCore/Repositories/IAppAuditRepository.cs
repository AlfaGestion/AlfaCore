using AlfaCore.Models;

namespace AlfaCore.Repositories;

public interface IAppAuditRepository
{
    Task WriteAsync(AppEventRecord auditEvent, IReadOnlyList<AuditChangeWriteDto> changes, CancellationToken ct = default);
    Task<AuditActivityPageDto> GetActivityAsync(string entityType, string recordId, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<AuditSchemaAvailabilityDto> CheckAvailabilityAsync(CancellationToken ct = default);
}
