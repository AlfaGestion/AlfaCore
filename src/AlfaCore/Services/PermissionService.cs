using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PermissionService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IPermissionService
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlySet<string>?> GetAllowedTaskKeysAsync(CancellationToken ct = default)
    {
        try
        {
            var userName = appUserSession.CurrentUser?.UserName?.Trim();
            var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            if (!await TableExistsAsync(cn, "ALFACORE_TAREAS_WEB", ct))
                return null;

            var sql = """
                SELECT DISTINCT UPPER(LTRIM(RTRIM(Clave)))
                FROM dbo.ALFACORE_TAREAS_WEB
                WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario
                  AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(Clave, '') <> '';
                """;

            var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant()
            }, commandTimeout: 5, cancellationToken: ct));

            return (IReadOnlySet<string>)rows
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            await TryLogWarningAsync(
                "Shell",
                "GetAllowedTaskKeys",
                ex,
                "No se pudieron resolver los permisos del usuario para el shell web.",
                ct);

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<bool> HasAccessAsync(string clave, CancellationToken ct = default)
    {
        var allowed = await GetAllowedTaskKeysAsync(ct);
        return allowed is null || allowed.Contains((clave ?? string.Empty).Trim().ToUpperInvariant());
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.tables
            WHERE object_id = OBJECT_ID(@FullName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { FullName = $"dbo.{tableName}" }, commandTimeout: 3, cancellationToken: ct));
        return count > 0;
    }

    private async Task TryLogWarningAsync(
        string process,
        string action,
        Exception exception,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            await appEvents.LogErrorAsync(
                process,
                action,
                exception,
                userMessage,
                new { User = appUserSession.CurrentUser?.UserName, System = appUserSession.CurrentUser?.SystemCode },
                severity: AppEventSeverity.Warning,
                ct: timeoutCts.Token);
        }
        catch
        {
            // La prioridad es no bloquear el shell por un fallo de permisos o de logging.
        }
    }
}
