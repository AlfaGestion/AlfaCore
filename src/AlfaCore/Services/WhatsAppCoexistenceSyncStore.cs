using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class WhatsAppCoexistenceSyncStore(IConfiguration configuration, IHostEnvironment? environment = null) : IWhatsAppCoexistenceSyncStore
{
    private string ConnectionString => WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment);

    public async Task<bool> TryReserveAsync(int idBase, string phoneNumberId, Guid idOnboarding, WhatsAppCoexistenceSyncType syncType,
        WhatsAppCoexistenceSyncStatus initialStatus, DateTime nowUtc, CancellationToken ct = default)
    {
        var normalizedPhoneId = NormalizePhoneNumberId(phoneNumberId);
        const string sql = """
            INSERT INTO dbo.WhatsAppEmbeddedCoexistenceSync
                (IdBase, PhoneNumberId, SyncType, IdOnboarding, Status, FechaAltaUtc, FechaModificacionUtc)
            SELECT @IdBase, @PhoneNumberId, @SyncType, @IdOnboarding, @Status, @NowUtc, @NowUtc
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.WhatsAppEmbeddedCoexistenceSync WITH (UPDLOCK, HOLDLOCK)
                WHERE IdBase=@IdBase AND PhoneNumberId=@PhoneNumberId AND SyncType=@SyncType);
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var rows = await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            IdBase = idBase,
            PhoneNumberId = normalizedPhoneId,
            SyncType = ToDb(syncType),
            IdOnboarding = idOnboarding,
            Status = ToDb(initialStatus),
            NowUtc = nowUtc
        }, cancellationToken: ct));
        return rows == 1;
    }

    public async Task MarkRequestedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, string requestId, DateTime requestedAtUtc, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedCoexistenceSync
            SET Status='REQUESTED', RequestId=@RequestId, RequestedAtUtc=@RequestedAtUtc, FechaModificacionUtc=SYSUTCDATETIME()
            WHERE IdBase=@IdBase AND PhoneNumberId=@PhoneNumberId AND SyncType=@SyncType AND Status='PENDING';
            """;
        await ExecuteAsync(sql, idBase, phoneNumberId, syncType, new { RequestId = (requestId ?? string.Empty).Trim(), RequestedAtUtc = requestedAtUtc }, ct);
    }

    public async Task MarkFailedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, string errorCode, string errorSummary, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedCoexistenceSync
            SET Status='FAILED', ErrorCode=@ErrorCode, ErrorSummary=@ErrorSummary, FechaModificacionUtc=SYSUTCDATETIME()
            WHERE IdBase=@IdBase AND PhoneNumberId=@PhoneNumberId AND SyncType=@SyncType AND Status='PENDING';
            """;
        await ExecuteAsync(sql, idBase, phoneNumberId, syncType, new { ErrorCode = (errorCode ?? string.Empty).Trim(), ErrorSummary = errorSummary ?? string.Empty }, ct);
    }

    public async Task MarkInProgressAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedCoexistenceSync
            SET Status='IN_PROGRESS', FechaModificacionUtc=SYSUTCDATETIME()
            WHERE IdBase=@IdBase AND PhoneNumberId=@PhoneNumberId AND SyncType=@SyncType AND Status IN ('PENDING','REQUESTED');
            """;
        await ExecuteAsync(sql, idBase, phoneNumberId, syncType, new { }, ct);
    }

    public async Task MarkCompletedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, DateTime completedAtUtc, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedCoexistenceSync
            SET Status='COMPLETED', CompletedAtUtc=@CompletedAtUtc, FechaModificacionUtc=SYSUTCDATETIME()
            WHERE IdBase=@IdBase AND PhoneNumberId=@PhoneNumberId AND SyncType=@SyncType AND Status IN ('PENDING','REQUESTED','IN_PROGRESS');
            """;
        await ExecuteAsync(sql, idBase, phoneNumberId, syncType, new { CompletedAtUtc = completedAtUtc }, ct);
    }

    public async Task MarkDeclinedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, string errorCode, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedCoexistenceSync
            SET Status='DECLINED', ErrorCode=@ErrorCode, FechaModificacionUtc=SYSUTCDATETIME()
            WHERE IdBase=@IdBase AND PhoneNumberId=@PhoneNumberId AND SyncType=@SyncType AND Status IN ('PENDING','REQUESTED','IN_PROGRESS');
            """;
        await ExecuteAsync(sql, idBase, phoneNumberId, syncType, new { ErrorCode = (errorCode ?? string.Empty).Trim() }, ct);
    }

    public async Task<WhatsAppCoexistenceSyncDto?> GetAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM dbo.WhatsAppEmbeddedCoexistenceSync WHERE IdBase=@IdBase AND PhoneNumberId=@PhoneNumberId AND SyncType=@SyncType";
        await using var cn = new SqlConnection(ConnectionString);
        var row = await cn.QuerySingleOrDefaultAsync<SyncRow>(new CommandDefinition(sql, new { IdBase = idBase, PhoneNumberId = NormalizePhoneNumberId(phoneNumberId), SyncType = ToDb(syncType) }, cancellationToken: ct));
        return row?.ToDto();
    }

    public async Task<IReadOnlyList<WhatsAppCoexistenceSyncDto>> GetForBaseAsync(int idBase, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM dbo.WhatsAppEmbeddedCoexistenceSync WHERE IdBase=@IdBase";
        await using var cn = new SqlConnection(ConnectionString);
        var rows = await cn.QueryAsync<SyncRow>(new CommandDefinition(sql, new { IdBase = idBase }, cancellationToken: ct));
        return rows.Select(row => row.ToDto()).ToArray();
    }

    private async Task ExecuteAsync(string sql, int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, object extra, CancellationToken ct)
    {
        var data = new DynamicParameters(extra);
        data.Add("IdBase", idBase);
        data.Add("PhoneNumberId", NormalizePhoneNumberId(phoneNumberId));
        data.Add("SyncType", ToDb(syncType));
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, data, cancellationToken: ct));
    }

    private static string NormalizePhoneNumberId(string phoneNumberId) => (phoneNumberId ?? string.Empty).Trim();

    private static string ToDb(WhatsAppCoexistenceSyncType value) => value switch
    {
        WhatsAppCoexistenceSyncType.History => "HISTORY",
        WhatsAppCoexistenceSyncType.ContactState => "SMB_APP_STATE_SYNC",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static WhatsAppCoexistenceSyncType FromDbSyncType(string value) => value switch
    {
        "HISTORY" => WhatsAppCoexistenceSyncType.History,
        "SMB_APP_STATE_SYNC" => WhatsAppCoexistenceSyncType.ContactState,
        _ => throw new InvalidOperationException($"SyncType desconocido en dbo.WhatsAppEmbeddedCoexistenceSync: {value}.")
    };

    private static string ToDb(WhatsAppCoexistenceSyncStatus value) => value switch
    {
        WhatsAppCoexistenceSyncStatus.Pending => "PENDING",
        WhatsAppCoexistenceSyncStatus.Requested => "REQUESTED",
        WhatsAppCoexistenceSyncStatus.InProgress => "IN_PROGRESS",
        WhatsAppCoexistenceSyncStatus.Completed => "COMPLETED",
        WhatsAppCoexistenceSyncStatus.Declined => "DECLINED",
        WhatsAppCoexistenceSyncStatus.Failed => "FAILED",
        WhatsAppCoexistenceSyncStatus.Expired => "EXPIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static WhatsAppCoexistenceSyncStatus FromDbStatus(string value) => value switch
    {
        "PENDING" => WhatsAppCoexistenceSyncStatus.Pending,
        "REQUESTED" => WhatsAppCoexistenceSyncStatus.Requested,
        "IN_PROGRESS" => WhatsAppCoexistenceSyncStatus.InProgress,
        "COMPLETED" => WhatsAppCoexistenceSyncStatus.Completed,
        "DECLINED" => WhatsAppCoexistenceSyncStatus.Declined,
        "FAILED" => WhatsAppCoexistenceSyncStatus.Failed,
        "EXPIRED" => WhatsAppCoexistenceSyncStatus.Expired,
        _ => throw new InvalidOperationException($"Status desconocido en dbo.WhatsAppEmbeddedCoexistenceSync: {value}.")
    };

    private sealed class SyncRow
    {
        public int IdBase { get; set; }
        public string PhoneNumberId { get; set; } = "";
        public string SyncType { get; set; } = "";
        public Guid IdOnboarding { get; set; }
        public string Status { get; set; } = "";
        public string RequestId { get; set; } = "";
        public DateTime? RequestedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string ErrorCode { get; set; } = "";
        public string ErrorSummary { get; set; } = "";
        public DateTime FechaModificacionUtc { get; set; }

        public WhatsAppCoexistenceSyncDto ToDto() => new()
        {
            IdBase = IdBase,
            PhoneNumberId = PhoneNumberId,
            IdOnboarding = IdOnboarding,
            SyncType = FromDbSyncType(SyncType),
            Status = FromDbStatus(Status),
            RequestId = RequestId,
            RequestedAtUtc = RequestedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            ErrorCode = ErrorCode,
            ErrorSummary = ErrorSummary,
            ModifiedAtUtc = FechaModificacionUtc
        };
    }
}
