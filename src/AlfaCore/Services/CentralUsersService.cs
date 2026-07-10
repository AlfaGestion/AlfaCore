using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CentralUsersService(IConfiguration configuration, IAppEventService appEvents) : ICentralUsersService
{
    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<IReadOnlyList<UsuarioCentralGridDto>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                u.[user] AS UserName,
                u.idcliente AS IdCliente,
                ISNULL(c.nombre, '') AS RazonSocial
            FROM dbo.users u
            LEFT JOIN dbo.Clientes c ON c.idcliente = u.idcliente
            ORDER BY u.[user];
            """;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            var rows = await cn.QueryAsync<UsuarioCentralGridDto>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.ToArray();
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync("Central", "Users", ex, "No se pudieron leer los usuarios centrales.", ct: ct);
            throw;
        }
    }
}
