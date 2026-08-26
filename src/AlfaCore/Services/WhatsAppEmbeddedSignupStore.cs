using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class WhatsAppEmbeddedSignupStore(IConfiguration configuration, IHostEnvironment? environment = null) : IWhatsAppEmbeddedSignupStore
{
    private string ConnectionString => WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment);

    public async Task CreateAsync(WhatsAppEmbeddedOnboardingDto item, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.WhatsAppEmbeddedOnboarding
            (IdOnboarding, IdBase, IdCliente, UsuarioIniciador, CorrelationId, StateHash, Estado, PasoActual,
             FechaInicioUtc, FechaExpiracionUtc, FechaModificacionUtc, NextAttemptUtc)
            VALUES
            (@IdOnboarding, @IdBase, @IdCliente, @UsuarioIniciador, @CorrelationId, @StateHash, @Estado, @PasoActual,
             @StartedAtUtc, @ExpiresAtUtc, @ModifiedAtUtc, @NextAttemptUtc);
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            item.IdOnboarding, item.IdBase, item.IdCliente, item.UsuarioIniciador, item.CorrelationId, item.StateHash,
            Estado = ToDb(item.Status), PasoActual = item.CurrentStep, item.StartedAtUtc, item.ExpiresAtUtc, item.ModifiedAtUtc, item.NextAttemptUtc
        }, cancellationToken: ct));
    }

    public async Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        return await GetInternalAsync(cn, idOnboarding, null, ct);
    }

    public async Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedOnboarding WITH (UPDLOCK, ROWLOCK)
            SET StateConsumedAtUtc = @NowUtc, FechaModificacionUtc = @NowUtc
            OUTPUT INSERTED.IdOnboarding
            WHERE StateHash = @StateHash
              AND UsuarioIniciador = @Usuario
              AND IdBase = @IdBase
              AND StateConsumedAtUtc IS NULL
              AND FechaExpiracionUtc > @NowUtc
              AND Estado = 'STARTED';
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var id = await cn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(sql, new { StateHash = stateHash, IdBase = idBase, Usuario = usuario, NowUtc = nowUtc }, cancellationToken: ct));
        return id.HasValue ? await GetInternalAsync(cn, id.Value, null, ct) : null;
    }

    public async Task UpdateStatusAsync(Guid id, WhatsAppEmbeddedOnboardingStatus expected, WhatsAppEmbeddedOnboardingStatus next, string step, CancellationToken ct = default)
    {
        WhatsAppEmbeddedSignupStateMachine.EnsureTransition(expected, next);
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedOnboarding
            SET Estado=@Next, PasoActual=@Step, FechaModificacionUtc=SYSUTCDATETIME()
            WHERE IdOnboarding=@Id AND Estado=@Expected;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var rows = await cn.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Expected = ToDb(expected), Next = ToDb(next), Step = step }, cancellationToken: ct));
        if (rows != 1) throw new InvalidOperationException("El onboarding cambió concurrentemente o no existe.");
    }

    public Task MarkAuthorizedAsync(Guid id, string tokenReference, string metaBusinessId, CancellationToken ct = default)
        => UpdateFieldsAsync(id, WhatsAppEmbeddedOnboardingStatus.Authorized, "AUTHORIZED", new { TokenReference = tokenReference, MetaBusinessId = metaBusinessId }, ct);

    public Task MarkActionRequiredAsync(Guid id, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default)
        => UpdateFieldsAsync(id, WhatsAppEmbeddedOnboardingStatus.ActionRequired, "ACTION_REQUIRED", new { ActionRequiredReason = ToDb(reason), ErrorSummary = summary, IncidentId = incidentId }, ct);

    public Task MarkRetryableFailureAsync(Guid id, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default)
        => UpdateFieldsAsync(id, WhatsAppEmbeddedOnboardingStatus.FailedRetryable, "RETRY_SCHEDULED", new { ErrorCode = errorCode, ErrorSummary = summary, IncidentId = incidentId, NextAttemptUtc = nextAttemptUtc, IncrementRetry = true }, ct);

    public Task MarkFinalFailureAsync(Guid id, string errorCode, string summary, string incidentId, CancellationToken ct = default)
        => UpdateFieldsAsync(id, WhatsAppEmbeddedOnboardingStatus.FailedFinal, "FAILED", new { ErrorCode = errorCode, ErrorSummary = summary, IncidentId = incidentId }, ct);

    public Task MarkReadyAsync(Guid id, CancellationToken ct = default)
        => UpdateFieldsAsync(id, WhatsAppEmbeddedOnboardingStatus.Ready, "READY", new { }, ct);

    public async Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
    {
        const string sql = """
            ;WITH next_item AS
            (
                SELECT TOP (1) * FROM dbo.WhatsAppEmbeddedOnboarding WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Estado IN ('AUTHORIZED','DISCOVERING_ASSETS','VALIDATING_OWNERSHIP','CONFIGURING_ACCESS','SUBSCRIBING_WABAS',
                                 'CHECKING_CUSTOMER_PAYMENT','DISCOVERING_PHONES','REGISTERING_PHONES','IMPORTING','FAILED_RETRYABLE')
                  AND (NextAttemptUtc IS NULL OR NextAttemptUtc <= @NowUtc)
                  AND (ClaimExpiresAtUtc IS NULL OR ClaimExpiresAtUtc <= @NowUtc)
                  AND FechaExpiracionUtc > @NowUtc
                ORDER BY ISNULL(NextAttemptUtc, FechaModificacionUtc), FechaModificacionUtc
            )
            UPDATE next_item SET ClaimedBy=@WorkerId, ClaimExpiresAtUtc=@ClaimExpiresAtUtc, FechaModificacionUtc=@NowUtc
            OUTPUT INSERTED.IdOnboarding;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var id = await cn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(sql, new { WorkerId = workerId, NowUtc = nowUtc, ClaimExpiresAtUtc = claimExpiresAtUtc }, cancellationToken: ct));
        return id.HasValue ? await GetInternalAsync(cn, id.Value, null, ct) : null;
    }

    public async Task ReleaseClaimAsync(Guid id, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.WhatsAppEmbeddedOnboarding
            SET ClaimedBy=NULL, ClaimExpiresAtUtc=NULL, NextAttemptUtc=@NextAttemptUtc, FechaModificacionUtc=SYSUTCDATETIME()
            WHERE IdOnboarding=@Id AND ClaimedBy=@WorkerId;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { Id = id, WorkerId = workerId, NextAttemptUtc = nextAttemptUtc }, cancellationToken: ct));
    }

    private async Task UpdateFieldsAsync(Guid id, WhatsAppEmbeddedOnboardingStatus status, string step, object values, CancellationToken ct)
    {
        var data = new DynamicParameters(values);
        data.Add("Id", id); data.Add("Estado", ToDb(status)); data.Add("Paso", step);
        var assignments = new List<string> { "Estado=@Estado", "PasoActual=@Paso", "FechaModificacionUtc=SYSUTCDATETIME()" };
        foreach (var name in data.ParameterNames.Where(x => x is not "Id" and not "Estado" and not "Paso" and not "IncrementRetry")) assignments.Add($"{name}=@{name}");
        if (data.ParameterNames.Contains("IncrementRetry")) assignments.Add("RetryCount=RetryCount+1");
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition($"UPDATE dbo.WhatsAppEmbeddedOnboarding SET {string.Join(',', assignments)} WHERE IdOnboarding=@Id", data, cancellationToken: ct));
    }

    private static async Task<WhatsAppEmbeddedOnboardingDto?> GetInternalAsync(SqlConnection cn, Guid id, SqlTransaction? tx, CancellationToken ct)
    {
        const string sql = "SELECT * FROM dbo.WhatsAppEmbeddedOnboarding WHERE IdOnboarding=@Id";
        var row = await cn.QuerySingleOrDefaultAsync<OnboardingRow>(new CommandDefinition(sql, new { Id = id }, tx, cancellationToken: ct));
        return row?.ToDto();
    }

    internal static string ToDb(Enum value) => string.Concat(value.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();

    private sealed class OnboardingRow
    {
        public Guid IdOnboarding { get; set; } public int IdBase { get; set; } public string IdCliente { get; set; } = ""; public string UsuarioIniciador { get; set; } = "";
        public string CorrelationId { get; set; } = ""; public string StateHash { get; set; } = ""; public DateTime? StateConsumedAtUtc { get; set; }
        public string Estado { get; set; } = ""; public string PasoActual { get; set; } = ""; public string MetaBusinessId { get; set; } = "";
        public DateTime FechaInicioUtc { get; set; } public DateTime FechaExpiracionUtc { get; set; } public DateTime FechaModificacionUtc { get; set; }
        public int RetryCount { get; set; } public DateTime? NextAttemptUtc { get; set; } public string ErrorCode { get; set; } = ""; public string ErrorSummary { get; set; } = "";
        public string IncidentId { get; set; } = ""; public string TokenReference { get; set; } = ""; public string? ActionRequiredReason { get; set; }
        public string ClaimedBy { get; set; } = ""; public DateTime? ClaimExpiresAtUtc { get; set; } public byte[] RowVersion { get; set; } = [];
        public WhatsAppEmbeddedOnboardingDto ToDto() => new()
        {
            IdOnboarding=IdOnboarding, IdBase=IdBase, IdCliente=IdCliente, UsuarioIniciador=UsuarioIniciador, CorrelationId=CorrelationId, StateHash=StateHash,
            StateConsumedAtUtc=StateConsumedAtUtc, Status=Enum.Parse<WhatsAppEmbeddedOnboardingStatus>(Estado.Replace("_", ""), true), CurrentStep=PasoActual,
            MetaBusinessId=MetaBusinessId, StartedAtUtc=FechaInicioUtc, ExpiresAtUtc=FechaExpiracionUtc, ModifiedAtUtc=FechaModificacionUtc, RetryCount=RetryCount,
            NextAttemptUtc=NextAttemptUtc, ErrorCode=ErrorCode, ErrorSummary=ErrorSummary, IncidentId=IncidentId, TokenReference=TokenReference,
            ActionRequiredReason=string.IsNullOrWhiteSpace(ActionRequiredReason)?null:Enum.Parse<WhatsAppEmbeddedActionRequiredReason>(ActionRequiredReason.Replace("_", ""), true),
            ClaimedBy=ClaimedBy, ClaimExpiresAtUtc=ClaimExpiresAtUtc, RowVersion=RowVersion
        };
    }
}
