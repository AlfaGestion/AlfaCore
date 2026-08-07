using System.Security.Cryptography;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CentralBasesService(IConfiguration configuration, IAppEventService appEvents) : ICentralBasesService
{
    private const string SelectColumns = """
        id AS IdBase,
        idcliente AS IdCliente,
        ISNULL(nombre, '') AS Nombre,
        ISNULL(dbserver, '') AS DbServer,
        ISNULL(dbname, '') AS DbName,
        ISNULL(dbuser, '') AS DbUser,
        ISNULL(dbpassword, '') AS DbPassword,
        WebhookToken
        """;

    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
        => QuerySingleAsync("WHERE id = @IdBase", new { IdBase = idBase }, ct);

    public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(webhookToken))
        {
            return Task.FromResult<BaseCentralDto?>(null);
        }

        return QuerySingleAsync("WHERE WebhookToken = @WebhookToken", new { WebhookToken = webhookToken.Trim() }, ct);
    }

    public async Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(idBase, ct);
        if (existing is null)
        {
            throw new InvalidOperationException($"No existe la base central {idBase}.");
        }

        if (!string.IsNullOrWhiteSpace(existing.WebhookToken))
        {
            return existing.WebhookToken;
        }

        var newToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        const string sql = """
            UPDATE dbo.bases
            SET WebhookToken = @WebhookToken
            WHERE id = @IdBase
              AND WebhookToken IS NULL;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { IdBase = idBase, WebhookToken = newToken }, cancellationToken: ct))
            .ConfigureAwait(false);

        // Si dos requests concurrentes pidieron el token al mismo tiempo, el UPDATE con
        // "WHERE WebhookToken IS NULL" solo lo aplica una vez — releemos para devolver siempre
        // el valor que efectivamente quedó grabado.
        var refreshed = await GetByIdAsync(idBase, ct);
        return refreshed?.WebhookToken
            ?? throw new InvalidOperationException($"No se pudo generar el WebhookToken de la base {idBase}.");
    }

    public async Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
    {
        var sql = includeAllForSuperAdmin
            ? $"""
              SELECT {SelectColumns}
              FROM dbo.bases
              ORDER BY ISNULL(nombre, ''), id;
              """
            : $"""
              SELECT {SelectColumns}
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
        var sql = $"""
            SELECT {SelectColumns}
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
            SELECT TOP (1) {SelectColumns}
            FROM dbo.bases
            {filterSql};
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var row = await cn.QuerySingleOrDefaultAsync<BaseCentralDto>(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        return row;
    }
}
