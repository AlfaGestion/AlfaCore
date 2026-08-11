using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Repositories;

public sealed class AppAuditRepository(
    IConfiguration configuration,
    ISessionService sessionService) : IAppAuditRepository
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task WriteAsync(
        AppEventRecord auditEvent,
        IReadOnlyList<AuditChangeWriteDto> changes,
        CancellationToken ct = default)
    {
        const string insertEventSql = """
            INSERT INTO dbo.SYS_EventosAplicacion
            (
                Id, FechaHora, Tipo, Severidad, Modulo, Accion,
                EntidadTipo, EntidadId, Usuario, ServidorSql, BaseDatos,
                RequestPath, HttpMethod, TraceId, CorrelationId,
                Mensaje, MensajeUsuario, DataJson
            )
            VALUES
            (
                @Id, @Timestamp, N'Audit', @Severity, @Module, @Action,
                @EntityType, @EntityId, @UserName, @SessionServer, @SessionDatabase,
                @RequestPath, @HttpMethod, @TraceId, @CorrelationId,
                @Message, @UserMessage, @DataJson
            );
            """;

        const string insertChangeSql = """
            INSERT INTO dbo.SYS_EventosAplicacionCambios
                (EventoId, Campo, ValorAnterior, ValorNuevo, Orden)
            VALUES
                (@AuditEventId, @FieldName, @OldValue, @NewValue, @Order);
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        try
        {
            await cn.ExecuteAsync(new CommandDefinition(
                insertEventSql,
                new
                {
                    auditEvent.Id,
                    auditEvent.Timestamp,
                    Severity = auditEvent.Severity.ToString(),
                    auditEvent.Module,
                    auditEvent.Action,
                    auditEvent.EntityType,
                    EntityId = auditEvent.EntityId,
                    auditEvent.UserName,
                    auditEvent.SessionServer,
                    auditEvent.SessionDatabase,
                    auditEvent.RequestPath,
                    auditEvent.HttpMethod,
                    auditEvent.TraceId,
                    auditEvent.CorrelationId,
                    auditEvent.Message,
                    auditEvent.UserMessage,
                    auditEvent.DataJson
                },
                tx,
                cancellationToken: ct));

            if (changes.Count > 0)
            {
                var parameters = changes.Select(change => new
                {
                    AuditEventId = auditEvent.Id,
                    change.FieldName,
                    change.OldValue,
                    change.NewValue,
                    change.Order
                });
                await cn.ExecuteAsync(new CommandDefinition(
                    insertChangeSql,
                    parameters,
                    tx,
                    cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AuditActivityPageDto> GetActivityAsync(
        string entityType,
        string recordId,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var offset = (pageNumber - 1) * pageSize;

        const string eventsSql = """
            SELECT COUNT_BIG(1)
            FROM dbo.SYS_EventosAplicacion
            WHERE Tipo = N'Audit'
              AND EntidadTipo = @EntityType
              AND EntidadId = @RecordId;

            SELECT
                Id,
                ISNULL(EntidadTipo, N'') AS EntityType,
                ISNULL(EntidadId, N'') AS RecordId,
                Accion AS Operation,
                ISNULL(Usuario, N'Sistema') AS UserName,
                FechaHora AS CreatedAt,
                ISNULL(Mensaje, N'') AS Message,
                ISNULL(DataJson, N'') AS MetadataJson
            FROM dbo.SYS_EventosAplicacion
            WHERE Tipo = N'Audit'
              AND EntidadTipo = @EntityType
              AND EntidadId = @RecordId
            ORDER BY FechaHora DESC, Id DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        const string changesSql = """
            SELECT
                Id,
                EventoId AS AuditEventId,
                Campo AS FieldName,
                ValorAnterior AS OldValue,
                ValorNuevo AS NewValue,
                Orden AS [Order]
            FROM dbo.SYS_EventosAplicacionCambios
            WHERE EventoId IN @EventIds
            ORDER BY EventoId, Orden, Id;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        using var grid = await cn.QueryMultipleAsync(new CommandDefinition(
            eventsSql,
            new { EntityType = entityType, RecordId = recordId, Offset = offset, PageSize = pageSize },
            cancellationToken: ct));

        var total = checked((int)await grid.ReadSingleAsync<long>());
        var events = (await grid.ReadAsync<AuditActivityEventDto>()).AsList();
        grid.Dispose();
        if (events.Count > 0)
        {
            var changes = (await cn.QueryAsync<AuditActivityChangeDto>(new CommandDefinition(
                changesSql,
                new { EventIds = events.Select(item => item.Id).ToArray() },
                cancellationToken: ct))).AsList();
            var byEvent = changes.GroupBy(change => change.AuditEventId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<AuditActivityChangeDto>)group.ToList());
            foreach (var item in events)
                item.Changes = byEvent.GetValueOrDefault(item.Id, []);
        }

        return new AuditActivityPageDto
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            Total = total,
            Items = events
        };
    }

    public async Task<AuditSchemaAvailabilityDto> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                CASE WHEN OBJECT_ID(N'dbo.SYS_EventosAplicacion', N'U') IS NOT NULL THEN 1 ELSE 0 END AS HasEvents,
                CASE WHEN OBJECT_ID(N'dbo.SYS_EventosAplicacionCambios', N'U') IS NOT NULL THEN 1 ELSE 0 END AS HasChanges,
                CASE WHEN NOT EXISTS
                (
                    SELECT required.Name
                    FROM (VALUES
                        (N'Id'), (N'FechaHora'), (N'Tipo'), (N'Severidad'), (N'Modulo'), (N'Accion'),
                        (N'EntidadTipo'), (N'EntidadId'), (N'Usuario'), (N'ServidorSql'), (N'BaseDatos'),
                        (N'RequestPath'), (N'HttpMethod'), (N'TraceId'), (N'CorrelationId'),
                        (N'Mensaje'), (N'MensajeUsuario'), (N'DataJson')
                    ) required(Name)
                    EXCEPT
                    SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacion')
                ) THEN 1 ELSE 0 END AS HasEventShape,
                CASE WHEN NOT EXISTS
                (
                    SELECT required.Name
                    FROM (VALUES (N'Id'), (N'EventoId'), (N'Campo'), (N'ValorAnterior'), (N'ValorNuevo'), (N'Orden')) required(Name)
                    EXCEPT
                    SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacionCambios')
                ) THEN 1 ELSE 0 END AS HasChangeShape;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var state = await cn.QuerySingleAsync<SchemaState>(new CommandDefinition(sql, cancellationToken: ct));
        var missing = new List<string>();
        if (!state.HasEvents) missing.Add("dbo.SYS_EventosAplicacion");
        if (!state.HasChanges) missing.Add("dbo.SYS_EventosAplicacionCambios");
        if (!state.HasEventShape) missing.Add("columnas requeridas de SYS_EventosAplicacion");
        if (!state.HasChangeShape) missing.Add("columnas requeridas de SYS_EventosAplicacionCambios");
        return new AuditSchemaAvailabilityDto { Available = missing.Count == 0, MissingObjects = missing };
    }

    private sealed class SchemaState
    {
        public bool HasEvents { get; init; }
        public bool HasChanges { get; init; }
        public bool HasEventShape { get; init; }
        public bool HasChangeShape { get; init; }
    }
}
