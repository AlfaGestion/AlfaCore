using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ComprasDashboardService
{
    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private SqlCommand CreateCommand(SqlConnection connection, string sql)
    {
        var command = new SqlCommand(sql, connection);
        command.CommandTimeout = Math.Max(5, _sqlOptions.CommandTimeoutSegundos);
        return command;
    }

    private void AddCommonParameters(SqlCommand command, DashboardFilters filters)
    {
        command.Parameters.AddWithValue("@FechaDesde", (object?)filters.FechaDesde ?? DBNull.Value);
        command.Parameters.AddWithValue("@FechaHasta", (object?)filters.FechaHasta ?? DBNull.Value);
        command.Parameters.AddWithValue("@Proveedor", ToDbValue(filters.Proveedor));
        command.Parameters.AddWithValue("@Articulo", ToDbValue(filters.Articulo));
        command.Parameters.AddWithValue("@ArticuloCodigo", ToDbValue(filters.ArticuloCodigo));
        command.Parameters.AddWithValue("@ArticuloDescripcion", ToDbValue(filters.ArticuloDescripcion));
        command.Parameters.AddWithValue("@Rubro", ToDbValue(filters.Rubro));
        command.Parameters.AddWithValue("@Familia", ToDbValue(filters.Familia));
        command.Parameters.AddWithValue("@Usuario", ToDbValue(filters.Usuario));
        command.Parameters.AddWithValue("@Sucursal", ToDbValue(filters.Sucursal));
        command.Parameters.AddWithValue("@Deposito", ToDbValue(filters.Deposito));
        command.Parameters.AddWithValue("@Estado", ToDbValue(filters.Estado));
        command.Parameters.AddWithValue("@TipoComprobante", ToDbValue(filters.TipoComprobante));
    }

    private SqlCommand BuildCommand(SqlConnection connection, string sql, DashboardFilters filters)
    {
        var command = CreateCommand(connection, sql);
        AddCommonParameters(command, filters);
        return command;
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        SqlConnection connection,
        string sql,
        DashboardFilters filters,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(connection, sql, filters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(map(reader));
        }

        return items;
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        SqlConnection connection,
        string sql,
        Action<SqlCommand> configure,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, sql);
        configure(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(map(reader));
        }

        return items;
    }

    private async Task<T?> ReadSingleAsync<T>(
        SqlConnection connection,
        string sql,
        DashboardFilters filters,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(connection, sql, filters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? map(reader) : default;
    }

    private async Task<T?> ReadSingleAsync<T>(
        SqlConnection connection,
        string sql,
        Action<SqlCommand> configure,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, sql);
        configure(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? map(reader) : default;
    }

    private async Task<IReadOnlyList<string>> ReadDistinctAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0)) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(value);
            }
        }

        return items;
    }

    private async Task<IReadOnlyList<CategoryTotalDto>> GetCategoryTotalsAsync(
        SqlConnection connection,
        DashboardFilters filters,
        string sql,
        decimal totalGeneral,
        CancellationToken cancellationToken)
    {
        var divisor = totalGeneral == 0 ? 1 : totalGeneral;
        var items = await ReadListAsync(connection, sql, filters, reader => new CategoryTotalDto
        {
            Categoria = reader.SafeGetString("Categoria"),
            Codigo = reader.SafeGetString("Codigo"),
            Total = reader.GetDecimal("Total")
        }, cancellationToken);

        return items.Select(x => new CategoryTotalDto
        {
            Categoria = x.Categoria,
            Codigo = x.Codigo,
            Total = x.Total,
            Participacion = x.Total / divisor
        }).ToList();
    }

    private async Task<IReadOnlyList<MonthlyPointDto>> GetMonthlySeriesAsync(
        SqlConnection connection,
        DashboardFilters filters,
        string valueExpression,
        string fromClause,
        string dateColumn,
        CancellationToken cancellationToken)
    {
            var sql = $"""
                SELECT TOP (12)
                RIGHT('0' + CONVERT(varchar(2), MONTH({dateColumn})), 2) + '/' + CONVERT(varchar(4), YEAR({dateColumn})) AS Periodo,
                {valueExpression} AS Total
            {fromClause}
                GROUP BY YEAR({dateColumn}), MONTH({dateColumn})
                ORDER BY YEAR({dateColumn}) DESC, MONTH({dateColumn}) DESC;
            """;

        var items = await ReadListAsync(connection, sql, filters, reader => new MonthlyPointDto
        {
            Periodo = reader.SafeGetString("Periodo"),
            Total = reader.GetDecimal("Total")
        }, cancellationToken);

        return items.Reverse<MonthlyPointDto>().ToList();
    }

    private static IReadOnlyList<MonthlyPointDto> NormalizeLast12Months(IReadOnlyList<MonthlyPointDto> items, DateTime? referenceDate = null)
    {
        var today = referenceDate ?? DateTime.Today;
        var startMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-11);

        var lookup = items
            .GroupBy(x => x.Periodo)
            .ToDictionary(g => g.Key, g => g.Last().Total, StringComparer.OrdinalIgnoreCase);

        var normalized = new List<MonthlyPointDto>(12);
        for (var i = 0; i < 12; i++)
        {
            var month = startMonth.AddMonths(i);
            var key = month.ToString("MM/yyyy");
            normalized.Add(new MonthlyPointDto
            {
                Periodo = key,
                Total = lookup.TryGetValue(key, out var value) ? value : 0m
            });
        }

        return normalized;
    }
}
