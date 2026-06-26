using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CentralBasesService(IConfiguration configuration, IAppEventService appEvents) : ICentralBasesService
{
    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
        => QuerySingleAsync("WHERE id = @IdBase", new { IdBase = idBase }, ct);

    public async Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
    {
        var sql = includeAllForSuperAdmin
            ? """
              SELECT
                  id AS IdBase,
                  idcliente AS IdCliente,
                  ISNULL(nombre, '') AS Nombre,
                  ISNULL(dbserver, '') AS DbServer,
                  ISNULL(dbname, '') AS DbName,
                  ISNULL(dbuser, '') AS DbUser,
                  ISNULL(dbpassword, '') AS DbPassword
              FROM dbo.bases
              ORDER BY ISNULL(nombre, ''), id;
              """
            : """
              SELECT
                  id AS IdBase,
                  idcliente AS IdCliente,
                  ISNULL(nombre, '') AS Nombre,
                  ISNULL(dbserver, '') AS DbServer,
                  ISNULL(dbname, '') AS DbName,
                  ISNULL(dbuser, '') AS DbUser,
                  ISNULL(dbpassword, '') AS DbPassword
              FROM dbo.bases
              WHERE idcliente = @IdCliente
              ORDER BY ISNULL(nombre, ''), id;
              """;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            var rows = await cn.QueryAsync<BaseCentralDto>(new CommandDefinition(sql, new { IdCliente = idCliente }, cancellationToken: ct)).ConfigureAwait(false);
            return rows.ToArray();
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync("Central", "Bases", ex, "No se pudieron leer las bases centrales.", new { idCliente, includeAllForSuperAdmin }, AppEventSeverity.Error, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                id AS IdBase,
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS Nombre,
                ISNULL(dbserver, '') AS DbServer,
                ISNULL(dbname, '') AS DbName,
                ISNULL(dbuser, '') AS DbUser,
                ISNULL(dbpassword, '') AS DbPassword
            FROM dbo.bases
            ORDER BY idcliente, ISNULL(nombre, ''), id;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<BaseCentralDto>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToArray();
    }

    private async Task<BaseCentralDto?> QuerySingleAsync(string filterSql, object parameters, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                id AS IdBase,
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS Nombre,
                ISNULL(dbserver, '') AS DbServer,
                ISNULL(dbname, '') AS DbName,
                ISNULL(dbuser, '') AS DbUser,
                ISNULL(dbpassword, '') AS DbPassword
            FROM dbo.bases
            {filterSql};
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var row = await cn.QuerySingleOrDefaultAsync<BaseCentralDto>(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        return row;
    }
}
