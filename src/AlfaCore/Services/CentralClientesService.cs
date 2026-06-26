using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CentralClientesService(IConfiguration configuration, IAppEventService appEvents) : ICentralClientesService
{
    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public Task<ClienteCentralDto?> GetByIdClienteAsync(string idCliente, CancellationToken ct = default)
        => QuerySingleAsync("WHERE idcliente = @IdCliente", new { IdCliente = idCliente }, ct);

    public Task<ClienteCentralDto?> GetByIdWebAsync(string idWeb, CancellationToken ct = default)
        => QuerySingleAsync("WHERE UPPER(LTRIM(RTRIM(idweb))) = UPPER(LTRIM(RTRIM(@IdWeb)))", new { IdWeb = idWeb }, ct);

    public async Task<IReadOnlyList<ClienteCentralDto>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS RazonSocial,
                ISNULL(idweb, '') AS IdWeb,
                ISNULL(superadmin, 0) AS SuperAdmin
            FROM dbo.Clientes
            ORDER BY ISNULL(nombre, ''), idcliente;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await cn.QueryAsync<ClienteCentralRow>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    private async Task<ClienteCentralDto?> QuerySingleAsync(string filterSql, object parameters, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                idcliente AS IdCliente,
                ISNULL(nombre, '') AS RazonSocial,
                ISNULL(idweb, '') AS IdWeb,
                ISNULL(superadmin, 0) AS SuperAdmin
            FROM dbo.Clientes
            {filterSql};
            """;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            var row = await cn.QuerySingleOrDefaultAsync<ClienteCentralRow>(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
            return row is null ? null : Map(row);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync("Central", "Clientes", ex, "No se pudieron leer los clientes centrales.", new { filterSql, parameters }, AppEventSeverity.Error, ct);
            throw;
        }
    }

    private static ClienteCentralDto Map(ClienteCentralRow row)
        => new()
        {
            IdCliente = row.IdCliente,
            RazonSocial = row.RazonSocial ?? string.Empty,
            IdWeb = row.IdWeb ?? string.Empty,
            SuperAdmin = row.SuperAdmin
        };

    private sealed class ClienteCentralRow
    {
        public string IdCliente { get; set; } = string.Empty;
        public string? RazonSocial { get; set; }
        public string? IdWeb { get; set; }
        public bool SuperAdmin { get; set; }
    }
}
