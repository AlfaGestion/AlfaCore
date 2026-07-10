using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class FavoritesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IFavoritesService
{
    private const string ExcludedFavoritePrefix = "~";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<ShellMenuNodeDto>> GetFavoritesAsync(CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Shell", "GetFavorites", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "ALFACORE_MENU_WEB", token))
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

            var userName = appUserSession.CurrentUser?.UserName?.Trim();
            var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();

            var sql = string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode)
                ? $"""
                    SELECT
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
                        ISNULL(w.Observacion, '') AS Observacion
                    FROM dbo.ALFACORE_MENU_WEB w
                    {legacyJoin}
                    WHERE ISNULL(w.HabilitadoWeb, 1) = 1
                      AND ISNULL(w.EsFavoritoDefault, 0) = 1
                      AND ISNULL(w.RutaWeb, '') <> ''
                    ORDER BY ISNULL(w.OrdenWeb, 0), {nameExpr};
                    """
                : $"""
                    SELECT
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
                        ISNULL(w.Observacion, '') AS Observacion
                    FROM dbo.ALFACORE_MENU_WEB w
                    {legacyJoin}
                    LEFT JOIN dbo.ALFACORE_FAVORITOS f
                        ON f.Clave = w.Clave
                       AND UPPER(LTRIM(RTRIM(f.Usuario))) = @Usuario
                       AND UPPER(LTRIM(RTRIM(f.Sistema))) = @Sistema
                    LEFT JOIN dbo.ALFACORE_FAVORITOS fx
                        ON UPPER(LTRIM(RTRIM(fx.Clave))) = @ClaveExcluida + UPPER(LTRIM(RTRIM(w.Clave)))
                       AND UPPER(LTRIM(RTRIM(fx.Usuario))) = @Usuario
                       AND UPPER(LTRIM(RTRIM(fx.Sistema))) = @Sistema
                    WHERE ISNULL(w.HabilitadoWeb, 1) = 1
                      AND ISNULL(w.RutaWeb, '') <> ''
                      AND (f.Clave IS NOT NULL OR (ISNULL(w.EsFavoritoDefault, 0) = 1 AND fx.Clave IS NULL))
                    ORDER BY ISNULL(f.Orden, ISNULL(w.OrdenWeb, 0)), {nameExpr};
                    """;

            var rows = await cn.QueryAsync<ShellMenuNodeDto>(new CommandDefinition(sql, new
            {
                Usuario = userName?.ToUpperInvariant(),
                Sistema = systemCode?.ToUpperInvariant(),
                ClaveExcluida = ExcludedFavoritePrefix
            }, cancellationToken: token));

            return rows.ToArray();
        }, "No se pudieron cargar los favoritos del shell.", ct);
    }

    public async Task<bool> IsFavoriteAsync(string clave, CancellationToken ct = default)
    {
        var userName = appUserSession.CurrentUser?.UserName?.Trim();
        var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
            return false;

        return await ExecuteLoggedAsync("Shell", "IsFavorite", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "ALFACORE_FAVORITOS", token))
                return false;

            var sql = """
                SELECT CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.ALFACORE_FAVORITOS
                    WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                      AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                      AND UPPER(LTRIM(RTRIM(Clave))) = @Clave
                )
                OR
                (
                    EXISTS
                    (
                        SELECT 1
                        FROM dbo.ALFACORE_MENU_WEB
                        WHERE UPPER(LTRIM(RTRIM(Clave))) = @Clave
                          AND ISNULL(EsFavoritoDefault, 0) = 1
                    )
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.ALFACORE_FAVORITOS
                        WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                          AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                          AND UPPER(LTRIM(RTRIM(Clave))) = @ClaveExcluida
                    )
                )
                THEN 1 ELSE 0 END;
                """;

            var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant(),
                Clave = (clave ?? string.Empty).Trim().ToUpperInvariant(),
                ClaveExcluida = BuildExcludedFavoriteKey(clave).ToUpperInvariant()
            }, cancellationToken: token));

            return count > 0;
        }, "No se pudo verificar el favorito solicitado.", ct);
    }

    public async Task<IReadOnlyList<string>> GetFavoriteKeysAsync(CancellationToken ct = default)
    {
        var userName = appUserSession.CurrentUser?.UserName?.Trim();
        var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
            return [];

        return await ExecuteLoggedAsync("Shell", "GetFavoriteKeys", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "ALFACORE_FAVORITOS", token))
                return [];

            const string sql = """
                SELECT LTRIM(RTRIM(Clave)) AS Clave
                FROM dbo.ALFACORE_FAVORITOS
                WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                  AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                  AND LEFT(LTRIM(RTRIM(Clave)), 1) <> @ClaveExcluida
                ORDER BY ISNULL(Orden, 0), Clave;
                """;

            var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant(),
                ClaveExcluida = ExcludedFavoritePrefix
            }, cancellationToken: token));

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, "No se pudieron cargar las claves favoritas del shell.", ct);
    }

    public async Task ToggleFavoriteAsync(string menu, string clave, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Shell", "ToggleFavorite", async token =>
        {
            var userName = appUserSession.CurrentUser?.UserName?.Trim();
            var systemCode = appUserSession.CurrentUser?.SystemCode?.Trim();
            var normalizedClave = (clave ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(systemCode))
                return 0;
            if (string.IsNullOrWhiteSpace(normalizedClave))
                return 0;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "ALFACORE_FAVORITOS", token))
                return 0;

            var existsSql = """
                SELECT COUNT(1)
                FROM dbo.ALFACORE_FAVORITOS
                WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                  AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(Clave))) = @Clave;
                """;

            var args = new
            {
                Usuario = userName.ToUpperInvariant(),
                Sistema = systemCode.ToUpperInvariant(),
                Clave = normalizedClave.ToUpperInvariant(),
                ClaveExcluida = BuildExcludedFavoriteKey(normalizedClave).ToUpperInvariant()
            };

            var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(existsSql, args, cancellationToken: token)) > 0;
            var excludedExists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(existsSql, new
            {
                args.Usuario,
                args.Sistema,
                Clave = args.ClaveExcluida
            }, cancellationToken: token)) > 0;
            var isDefaultFavorite = await IsDefaultFavoriteAsync(cn, normalizedClave, token);

            if (excludedExists)
            {
                await DeleteFavoriteRowAsync(cn, userName, systemCode, BuildExcludedFavoriteKey(normalizedClave), token);
                return 0;
            }

            if (exists)
            {
                await DeleteFavoriteRowAsync(cn, userName, systemCode, normalizedClave, token);
                if (isDefaultFavorite)
                    await InsertFavoriteRowAsync(cn, userName, systemCode, BuildExcludedFavoriteKey(normalizedClave), token);
                return 0;
            }

            if (isDefaultFavorite)
                await InsertFavoriteRowAsync(cn, userName, systemCode, BuildExcludedFavoriteKey(normalizedClave), token);
            else
                await InsertFavoriteRowAsync(cn, userName, systemCode, normalizedClave, token);

            return 0;
        }, "No se pudo actualizar el favorito seleccionado.", ct);
    }

    private static async Task InsertFavoriteRowAsync(SqlConnection cn, string userName, string systemCode, string clave, CancellationToken token)
    {
        const string insertSql = """
                INSERT INTO dbo.ALFACORE_FAVORITOS (Usuario, Sistema, Clave, Orden)
                VALUES (@UsuarioRaw, @SistemaRaw, @ClaveRaw,
                    (SELECT ISNULL(MAX(Orden), 0) + 1
                     FROM dbo.ALFACORE_FAVORITOS
                     WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
                       AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema));
                """;

        await cn.ExecuteAsync(new CommandDefinition(insertSql, new
        {
            Usuario = userName.ToUpperInvariant(),
            Sistema = systemCode.ToUpperInvariant(),
            UsuarioRaw = userName,
            SistemaRaw = systemCode,
            ClaveRaw = (clave ?? string.Empty).Trim()
        }, cancellationToken: token));
    }

    private static async Task DeleteFavoriteRowAsync(SqlConnection cn, string userName, string systemCode, string clave, CancellationToken token)
    {
        const string deleteSql = """
            DELETE FROM dbo.ALFACORE_FAVORITOS
            WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario
              AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
              AND UPPER(LTRIM(RTRIM(Clave))) = @Clave;
            """;

        await cn.ExecuteAsync(new CommandDefinition(deleteSql, new
        {
            Usuario = userName.ToUpperInvariant(),
            Sistema = systemCode.ToUpperInvariant(),
            Clave = (clave ?? string.Empty).Trim().ToUpperInvariant()
        }, cancellationToken: token));
    }

    private static async Task<bool> IsDefaultFavoriteAsync(SqlConnection cn, string clave, CancellationToken token)
    {
        if (!await TableExistsAsync(cn, "ALFACORE_MENU_WEB", token))
            return false;

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.ALFACORE_MENU_WEB
            WHERE UPPER(LTRIM(RTRIM(Clave))) = @Clave
              AND ISNULL(EsFavoritoDefault, 0) = 1;
            """;

        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            Clave = (clave ?? string.Empty).Trim().ToUpperInvariant()
        }, cancellationToken: token)) > 0;
    }

    private static string BuildExcludedFavoriteKey(string? clave)
        => $"{ExcludedFavoritePrefix}{(clave ?? string.Empty).Trim()}";

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
