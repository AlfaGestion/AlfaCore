using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class RecentService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IRecentService
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<ShellMenuNodeDto>> GetRecentsAsync(int maxRows = 8, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Shell", "GetRecents", async token =>
        {
            var userName = appUserSession.CurrentUser?.UserName?.Trim();
            var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
                return [];

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "ALFACORE_RECIENTES", token) || !await TableExistsAsync(cn, "ALFACORE_MENU_WEB", token))
                return [];

            var hasNombreWeb = await ColumnExistsAsync(cn, "ALFACORE_MENU_WEB", "NombreWeb", token);
            var hasDescripcionWeb = await ColumnExistsAsync(cn, "ALFACORE_MENU_WEB", "DescripcionWeb", token);
            var hasPadreClave = await ColumnExistsAsync(cn, "ALFACORE_MENU_WEB", "PadreClave", token);
            var hasLegacyMenu = await TableExistsAsync(cn, "TA_MENU", token);
            var legacyJoin = hasLegacyMenu
                ? """
                LEFT JOIN dbo.TA_MENU tm
                    ON tm.Menu = w.Menu
                   AND tm.Clave = w.Clave
                """
                : string.Empty;
            var parentExpr = hasPadreClave
                ? (hasLegacyMenu
                    ? "ISNULL(NULLIF(w.PadreClave, ''), ISNULL(tm.Titulo, ''))"
                    : "ISNULL(w.PadreClave, '')")
                : (hasLegacyMenu ? "ISNULL(tm.Titulo, '')" : "''");
            var nameExpr = hasNombreWeb
                ? (hasLegacyMenu
                    ? "ISNULL(NULLIF(w.NombreWeb, ''), ISNULL(NULLIF(tm.Nombre, ''), w.Clave))"
                    : "ISNULL(NULLIF(w.NombreWeb, ''), w.Clave)")
                : (hasLegacyMenu ? "ISNULL(NULLIF(tm.Nombre, ''), ISNULL(w.Clave, ''))" : "ISNULL(w.Clave, '')");
            var descriptionExpr = hasDescripcionWeb
                ? (hasLegacyMenu
                    ? "ISNULL(NULLIF(w.DescripcionWeb, ''), ISNULL(CONVERT(nvarchar(500), tm.Descripcion), ISNULL(w.Observacion, '')))"
                    : "ISNULL(w.DescripcionWeb, '')")
                : (hasLegacyMenu
                    ? "ISNULL(CONVERT(nvarchar(500), tm.Descripcion), ISNULL(w.Observacion, ''))"
                    : "ISNULL(w.Observacion, '')");

            var sql = $"""
                ;WITH latest AS
                (
                    SELECT
                        r.Clave,
                        MAX(r.FechaHora) AS FechaHora
                    FROM dbo.ALFACORE_RECIENTES r
                    WHERE UPPER(LTRIM(RTRIM(r.Usuario))) = @Usuario
                      AND UPPER(LTRIM(RTRIM(r.Sistema))) = @Sistema
                    GROUP BY r.Clave
                )
                SELECT TOP (@MaxRows)
                    ISNULL(w.Menu, '') AS Menu,
                    ISNULL(w.Clave, '') AS Clave,
                    {parentExpr} AS Titulo,
                    {nameExpr} AS Nombre,
                    {descriptionExpr} AS Descripcion,
                    '' AS Proceso,
                    ISNULL(w.RutaWeb, '') AS RutaWeb,
                    ISNULL(w.Componente, '') AS Componente,
                    ISNULL(w.Icono, '') AS Icono,
                    ISNULL(w.OrdenWeb, 0) AS OrdenWeb,
                    ISNULL(w.EsFavoritoDefault, 0) AS EsFavoritoDefault,
                    ISNULL(w.Observacion, '') AS Observacion,
                    l.FechaHora AS UltimoAcceso
                FROM latest l
                INNER JOIN dbo.ALFACORE_MENU_WEB w
                    ON w.Clave = l.Clave
                {legacyJoin}
                WHERE ISNULL(w.HabilitadoWeb, 1) = 1
                  AND ISNULL(w.RutaWeb, '') <> ''
                ORDER BY l.FechaHora DESC, ISNULL(w.OrdenWeb, 0), {nameExpr};
                """;

            var rows = await cn.QueryAsync<ShellMenuNodeDto>(new CommandDefinition(sql, new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant(),
                MaxRows = Math.Clamp(maxRows, 1, 20)
            }, cancellationToken: token));

            return rows.ToArray();
        }, "No se pudieron cargar los accesos recientes del shell.", ct);
    }

    public async Task RegisterRecentAsync(string menu, string clave, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Shell", "RegisterRecent", async token =>
        {
            var userName = appUserSession.CurrentUser?.UserName?.Trim();
            var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
                return 0;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "ALFACORE_RECIENTES", token))
                return 0;

            var deleteSql = """
                DELETE FROM dbo.ALFACORE_RECIENTES
                WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                  AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(Clave))) = @Clave;
                """;

            var insertSql = """
                INSERT INTO dbo.ALFACORE_RECIENTES (Usuario, Sistema, Clave, FechaHora)
                VALUES (@UsuarioRaw, @SistemaRaw, @ClaveRaw, GETDATE());

                ;WITH overflow AS
                (
                    SELECT ID,
                           ROW_NUMBER() OVER (ORDER BY FechaHora DESC, ID DESC) AS rn
                    FROM dbo.ALFACORE_RECIENTES
                    WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                      AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                )
                DELETE FROM dbo.ALFACORE_RECIENTES
                WHERE ID IN (SELECT ID FROM overflow WHERE rn > 20);
                """;

            var args = new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant(),
                Clave = (clave ?? string.Empty).Trim().ToUpperInvariant(),
                UsuarioRaw = userName,
                SistemaRaw = systemCode,
                ClaveRaw = (clave ?? string.Empty).Trim()
            };

            await cn.ExecuteAsync(new CommandDefinition(deleteSql, args, cancellationToken: token));
            await cn.ExecuteAsync(new CommandDefinition(insertSql, args, cancellationToken: token));
            return 0;
        }, "No se pudo registrar el acceso reciente del shell.", ct);
    }

    private async Task<T> ExecuteLoggedAsync<T>(string process, string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(process, action, ex, userMessage, ct: ct);
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

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@FullName)
              AND name = @ColumnName;
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            FullName = $"dbo.{tableName}",
            ColumnName = columnName
        }, cancellationToken: ct));
        return count > 0;
    }
}
