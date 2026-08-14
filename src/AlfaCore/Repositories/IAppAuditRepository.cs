using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Repositories;

public interface IAppAuditRepository
{
    Task WriteAsync(AppEventRecord auditEvent, IReadOnlyList<AuditChangeWriteDto> changes, CancellationToken ct = default);
    Task WriteAsync(AppEventRecord auditEvent, IReadOnlyList<AuditChangeWriteDto> changes, SqlConnection connection, SqlTransaction transaction, CancellationToken ct = default);
    Task<AuditActivityPageDto> GetActivityAsync(string entityType, string recordId, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<AuditSchemaAvailabilityDto> CheckAvailabilityAsync(CancellationToken ct = default);
    Task<AuditSchemaAvailabilityDto> CheckAvailabilityAsync(SqlConnection connection, SqlTransaction? transaction = null, CancellationToken ct = default);
}
