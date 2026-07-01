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

    public async Task<string> GenerateAndSaveIdWebAsync(string idCliente, string razonSocial, CancellationToken ct = default)
    {
        var baseIdWeb = NormalizeToIdWeb(razonSocial);
        if (string.IsNullOrWhiteSpace(baseIdWeb))
            baseIdWeb = "cliente";

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var prefix = baseIdWeb.Length > 8 ? baseIdWeb[..8] : baseIdWeb;
        var existingRows = await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT LOWER(LTRIM(RTRIM(idweb))) FROM dbo.Clientes WHERE idweb LIKE @Prefix + '%' AND idweb IS NOT NULL;",
            new { Prefix = prefix },
            cancellationToken: ct)).ConfigureAwait(false);
        var existing = existingRows.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidate = baseIdWeb;
        if (existing.Contains(candidate))
        {
            var suffix = 2;
            do { candidate = prefix + suffix++; } while (existing.Contains(candidate) && suffix <= 99);
        }

        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Clientes SET idweb = @IdWeb WHERE idcliente = @IdCliente;",
            new { IdWeb = candidate, IdCliente = idCliente },
            cancellationToken: ct)).ConfigureAwait(false);

        return candidate;
    }

    private static string NormalizeToIdWeb(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var normalized = source.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(10);

        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            var lower = char.ToLowerInvariant(ch);
            if (char.IsLetterOrDigit(lower))
                builder.Append(lower);

            if (builder.Length == 10)
                break;
        }

        return builder.ToString();
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
