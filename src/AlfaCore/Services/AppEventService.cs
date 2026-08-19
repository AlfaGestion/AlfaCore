using AlfaCore.Models;
using AlfaCore.Repositories;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class AppEventService(
    IWebHostEnvironment env,
    IHttpContextAccessor httpContextAccessor,
    IAuxErrRepository auxErrRepository,
    IAppAuditRepository auditRepository,
    IAppAuditActorAccessor auditActor,
    ILogger<AppEventService> logger) : IAppEventService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _logDirectory = Path.Combine(env.ContentRootPath, "App_Data", "diagnostics");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> LogErrorAsync(
        string module,
        string action,
        Exception exception,
        string userMessage,
        object? data = null,
        AppEventSeverity severity = AppEventSeverity.Error,
        CancellationToken ct = default)
    {
        var eventId = Guid.NewGuid();
        var record = CreateBaseRecord(AppEventKind.Error, severity, module, action, userMessage, data, eventId);
        record.Message = exception.Message;
        record.ExceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        record.ExceptionMessage = exception.ToString();
        record.StackTrace = exception.StackTrace ?? string.Empty;

        logger.LogError(exception, "[{EventId}] {Module}/{Action}: {UserMessage}", eventId, module, action, userMessage);

        try
        {
            var auxErrId = await auxErrRepository.InsertAsync(CreateAuxErrEntry(record, exception), ct);
            record.EntityType = "AUX_ERR";
            record.EntityId = auxErrId > 0 ? auxErrId.ToString() : string.Empty;
            await WriteAsync(record, ct);
            return auxErrId > 0 ? auxErrId.ToString() : eventId.ToString("N");
        }
        catch (Exception insertEx)
        {
            if (IsSameSqlConnectionFailure(exception, insertEx))
            {
                logger.LogWarning(insertEx, "[{EventId}] No se pudo registrar en AUX_ERR el error {Module}/{Action} porque la conexión SQL activa no está disponible.", eventId, module, action);
            }
            else
            {
                logger.LogError(insertEx, "[{EventId}] No se pudo registrar en AUX_ERR el error {Module}/{Action}.", eventId, module, action);
            }

            record.DataJson = MergeData(record.DataJson, new
            {
                AuxErrFallback = true,
                AuxErrInsertError = insertEx.Message
            });
            await WriteAsync(record, ct);
            return eventId.ToString("N");
        }
    }

    public async Task<string> LogAuditAsync(
        string module,
        string action,
        string entityType,
        string entityId,
        string message,
        object? data = null,
        CancellationToken ct = default)
    {
        var eventId = Guid.NewGuid();
        var record = CreateBaseRecord(AppEventKind.Audit, AppEventSeverity.Info, module, action, message, data, eventId);
        record.Message = message;
        record.EntityType = entityType;
        record.EntityId = entityId;

        logger.LogInformation("[{EventId}] AUDIT {Module}/{Action} {EntityType} {EntityId}: {Message}",
            eventId, module, action, entityType, entityId, message);
        await WriteAsync(record, ct);
        return eventId.ToString("N");
    }

    public async Task<Guid> WriteAuditAsync(AuditWriteRequest request, CancellationToken ct = default)
    {
        var prepared = PrepareAudit(request);
        var availability = await auditRepository.CheckAvailabilityAsync(ct);
        if (!availability.Available)
            throw new InvalidOperationException($"El esquema de auditoría no está disponible: {string.Join(", ", availability.MissingObjects)}.");

        await auditRepository.WriteAsync(prepared.Record, prepared.Changes, ct);
        await WriteAsync(prepared.Record, ct);
        LogPersisted(prepared.Record, prepared.Changes.Length);
        return prepared.Record.Id;
    }

    public async Task<Guid> WriteAuditAsync(
        AuditWriteRequest request,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken ct = default)
    {
        var prepared = PrepareAudit(request);
        await auditRepository.WriteAsync(prepared.Record, prepared.Changes, connection, transaction, ct);
        LogPersisted(prepared.Record, prepared.Changes.Length);
        return prepared.Record.Id;
    }

    private (AppEventRecord Record, AuditChangeWriteDto[] Changes) PrepareAudit(AuditWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entityType = Required(request.EntityType, nameof(request.EntityType), 80);
        var recordId = Required(request.RecordId, nameof(request.RecordId), 120);
        var operation = Required(request.Operation, nameof(request.Operation), 120);
        var module = Required(request.Module, nameof(request.Module), 80);

        var changes = request.Changes
            .Where(change => !AuditDataProtector.IsSensitiveName(change.FieldName))
            .Select((change, index) => new AuditChangeWriteDto
            {
                FieldName = Required(change.FieldName, nameof(change.FieldName), 120),
                OldValue = AuditDataProtector.ProtectValue(change.FieldName, change.OldValue, AuditDataProtector.MaxChangeValueLength),
                NewValue = AuditDataProtector.ProtectValue(change.FieldName, change.NewValue, AuditDataProtector.MaxChangeValueLength),
                Order = change.Order > 0 ? change.Order : checked((short)(index + 1))
            })
            .ToArray();

        var safeMetadata = request.Metadata
            .Where(item => !AuditDataProtector.IsSensitiveName(item.Key))
            .ToDictionary(
                item => Required(item.Key, "Metadata.Key", 120),
                item => AuditDataProtector.ProtectValue(item.Key, item.Value, AuditDataProtector.MaxMetadataValueLength));

        var eventId = Guid.NewGuid();
        var record = CreateBaseRecord(
            AppEventKind.Audit,
            AppEventSeverity.Info,
            module,
            operation,
            Truncate(request.Message, 500),
            safeMetadata,
            eventId);
        record.EntityType = entityType;
        record.EntityId = recordId;
        record.Message = Truncate(request.Message, 1000);
        record.UserName = ResolveFunctionalUser();

        return (record, changes);
    }

    private void LogPersisted(AppEventRecord record, int changeCount)
    {
        logger.LogInformation(
            "[{EventId}] AUDIT persistida {Module}/{Operation} {EntityType} {RecordId} con {ChangeCount} cambio(s).",
            record.Id, record.Module, record.Action, record.EntityType, record.EntityId, changeCount);
    }

    /// <summary>
    /// Lectura de infraestructura. No debe exponerse como endpoint genérico: el módulo consumidor
    /// debe autorizar primero el acceso a la entidad solicitada.
    /// </summary>
    public Task<AuditActivityPageDto> GetActivityAsync(
        string entityType,
        string recordId,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => auditRepository.GetActivityAsync(
            Required(entityType, nameof(entityType), 80),
            Required(recordId, nameof(recordId), 120),
            Math.Max(1, pageNumber),
            Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100),
            ct);

    public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(CancellationToken ct = default)
        => auditRepository.CheckAvailabilityAsync(ct);

    public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(
        SqlConnection connection,
        SqlTransaction? transaction = null,
        CancellationToken ct = default)
        => auditRepository.CheckAvailabilityAsync(connection, transaction, ct);

    private AppEventRecord CreateBaseRecord(
        AppEventKind kind,
        AppEventSeverity severity,
        string module,
        string action,
        string userMessage,
        object? data,
        Guid eventId)
    {
        var http = httpContextAccessor.HttpContext;
        var userName = ResolveFunctionalUser();

        return new AppEventRecord
        {
            Id = eventId,
            Timestamp = DateTime.Now,
            Kind = kind,
            Severity = severity,
            Module = module,
            Action = action,
            UserName = userName ?? string.Empty,
            SessionServer = string.Empty,
            SessionDatabase = string.Empty,
            RequestPath = http?.Request.Path.Value ?? string.Empty,
            HttpMethod = http?.Request.Method ?? string.Empty,
            TraceId = Activity.Current?.TraceId.ToString() ?? http?.TraceIdentifier ?? string.Empty,
            CorrelationId = Activity.Current?.Id ?? http?.TraceIdentifier ?? eventId.ToString("N"),
            UserMessage = userMessage,
            DataJson = SerializeData(data)
        };
    }

    private string ResolveFunctionalUser()
    {
        var userName = auditActor.UserName.Trim();
        if (!string.IsNullOrWhiteSpace(userName))
            return userName;

        userName = httpContextAccessor.HttpContext?.User?.Identity?.Name?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(userName))
            return userName;

        var centralLogin = auditActor.CentralLogin.Trim();
        return string.IsNullOrWhiteSpace(centralLogin) ? "Sistema" : centralLogin;
    }

    private static string Required(string? value, string parameterName, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new ArgumentException("El valor es obligatorio.", parameterName);
        return Truncate(normalized, maxLength);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private AuxErrEntry CreateAuxErrEntry(AppEventRecord record, Exception exception)
    {
        var sqlEx = FindSqlException(exception);
        var process = $"{record.Module}.{record.Action}".Trim('.');
        var technicalDetail = BuildTechnicalDetail(record, exception);

        return new AuxErrEntry
        {
            Process = process,
            ErrorCode = sqlEx?.Number ?? 0,
            Description = string.IsNullOrWhiteSpace(record.UserMessage) ? record.Message : record.UserMessage,
            SqlDetail = technicalDetail,
            Pc = ResolvePc(record),
            UserName = record.UserName
        };
    }

    private async Task WriteAsync(AppEventRecord record, CancellationToken ct)
    {
        Directory.CreateDirectory(_logDirectory);
        var path = Path.Combine(_logDirectory, $"app-events-{DateTime.Now:yyyyMM}.jsonl");
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

        await _gate.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(path, line, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string SerializeData(object? data)
    {
        if (data is null)
            return string.Empty;

        try
        {
            return JsonSerializer.Serialize(data, JsonOptions);
        }
        catch
        {
            return data.ToString() ?? string.Empty;
        }
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is SqlException sqlException)
                return sqlException;
            current = current.InnerException!;
        }

        return null;
    }

    private static bool IsSameSqlConnectionFailure(Exception originalException, Exception auxErrInsertException)
    {
        var originalSql = FindSqlException(originalException);
        var auxErrSql = FindSqlException(auxErrInsertException);

        if (originalSql is null || auxErrSql is null)
            return false;

        return originalSql.Number == auxErrSql.Number
            && string.Equals(originalSql.Server, auxErrSql.Server, StringComparison.OrdinalIgnoreCase)
            && string.Equals(originalSql.Message, auxErrSql.Message, StringComparison.Ordinal);
    }

    private static string BuildTechnicalDetail(AppEventRecord record, Exception exception)
    {
        var parts = new List<string>
        {
            $"Mensaje: {exception.Message}",
            $"Tipo: {exception.GetType().FullName ?? exception.GetType().Name}"
        };

        if (!string.IsNullOrWhiteSpace(record.RequestPath))
            parts.Add($"Request: {record.HttpMethod} {record.RequestPath}");
        if (!string.IsNullOrWhiteSpace(record.SessionServer) || !string.IsNullOrWhiteSpace(record.SessionDatabase))
            parts.Add($"Sesion SQL: {record.SessionServer} / {record.SessionDatabase}");
        if (!string.IsNullOrWhiteSpace(record.TraceId))
            parts.Add($"Trace: {record.TraceId}");
        if (!string.IsNullOrWhiteSpace(record.DataJson))
            parts.Add($"Data: {record.DataJson}");
        if (!string.IsNullOrWhiteSpace(record.StackTrace))
            parts.Add($"Stack: {record.StackTrace}");

        return string.Join(Environment.NewLine, parts);
    }

    private string ResolvePc(AppEventRecord record)
    {
        var http = httpContextAccessor.HttpContext;
        var remoteIp = http?.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp))
            return $"{Environment.MachineName} [{remoteIp}]";

        try
        {
            var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            var ipv4 = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
            return string.IsNullOrWhiteSpace(ipv4) ? Environment.MachineName : $"{Environment.MachineName} [{ipv4}]";
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    private static string MergeData(string existingJson, object extraData)
    {
        if (string.IsNullOrWhiteSpace(existingJson))
            return SerializeData(extraData);

        return $"{existingJson} | {SerializeData(extraData)}";
    }
}
