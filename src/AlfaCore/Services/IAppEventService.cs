using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IAppEventService
{
    Task<string> LogErrorAsync(
        string module,
        string action,
        Exception exception,
        string userMessage,
        object? data = null,
        AppEventSeverity severity = AppEventSeverity.Error,
        CancellationToken ct = default);

    Task<string> LogAuditAsync(
        string module,
        string action,
        string entityType,
        string entityId,
        string message,
        object? data = null,
        CancellationToken ct = default);

    Task<Guid> WriteAuditAsync(AuditWriteRequest request, CancellationToken ct = default);
    Task<Guid> WriteAuditAsync(AuditWriteRequest request, SqlConnection connection, SqlTransaction transaction, CancellationToken ct = default);

    Task<AuditActivityPageDto> GetActivityAsync(
        string entityType,
        string recordId,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(CancellationToken ct = default);
    Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(SqlConnection connection, SqlTransaction? transaction = null, CancellationToken ct = default);
}
