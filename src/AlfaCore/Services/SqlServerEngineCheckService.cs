using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class SqlServerEngineCheckService(ISessionService sessionService, IConfiguration configuration)
    : ISqlServerEngineCheckService
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion") ?? string.Empty;

    public async Task<SqlServerEngineInfoDto> GetActiveEngineInfoAsync(CancellationToken ct = default)
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            return new SqlServerEngineInfoDto();

        try
        {
            await using var cn = new SqlConnection(connectionString);
            var row = await cn.QuerySingleAsync<(string? MajorVersionText, string FullVersion)>(new CommandDefinition("""
                SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS nvarchar(10)) AS MajorVersionText,
                       @@VERSION AS FullVersion;
                """, cancellationToken: ct));

            var versionMayor = int.TryParse(row.MajorVersionText, out var parsed) ? parsed : (int?)null;
            var primeraLinea = row.FullVersion.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? row.FullVersion;

            return new SqlServerEngineInfoDto
            {
                Descripcion = primeraLinea,
                VersionMayor = versionMayor,
                RequiereActualizacion = versionMayor.HasValue && versionMayor.Value < SqlServerEngineInfoDto.VersionMinimaRecomendada
            };
        }
        catch
        {
            // No bloquea nada si falla el chequeo -- es solo informativo.
            return new SqlServerEngineInfoDto();
        }
    }
}
