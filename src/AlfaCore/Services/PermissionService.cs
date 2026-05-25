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
        return await ExecuteLoggedAsync("Shell", "GetAllowedTaskKeys", async token =>
        {
            var userName = appUserSession.CurrentUser?.UserName?.Trim();
            var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_TAREAS", token))
                return null;

            var sql = """
                SELECT DISTINCT UPPER(LTRIM(RTRIM(TAREA)))
                FROM dbo.TA_TAREAS
                WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario
                  AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(TAREA, '') <> '';
                """;

            var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant()
            }, cancellationToken: token));

            return (IReadOnlySet<string>)rows
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }, "No se pudieron resolver los permisos del usuario para el shell web.", ct);
    }

    public async Task<bool> HasAccessAsync(string clave, CancellationToken ct = default)
    {
        var allowed = await GetAllowedTaskKeysAsync(ct);
        return allowed is null || allowed.Contains((clave ?? string.Empty).Trim().ToUpperInvariant());
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string process,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                process,
                action,
                ex,
                userMessage,
                new { User = appUserSession.CurrentUser?.UserName, System = appUserSession.CurrentUser?.SystemCode },
                ct: ct);

            throw new InvalidOperationException($"{userMessage} Código: {incidentId}", ex);
        }
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.tables
            WHERE object_id = OBJECT_ID(@FullName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { FullName = $"dbo.{tableName}" }, cancellationToken: ct));
        return count > 0;
    }
}
