using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Mismo criterio de logging que ConversacionesHabilitacionService: las operaciones internas
/// tiran excepciones simples, LoggedAsync las traduce a AppUserFacingException con incidente.</summary>
public sealed class WorkerAssignmentService(IConfiguration configuration, IAppUserSessionService user,
    IAppEventService events) : IWorkerAssignmentService
{
    private const string Clave = "WORKERS_SERVIDOR_ACTIVO";

    public string NombreServidorLocal { get; } = ResolverNombreServidorLocal(configuration);

    private string? ConnectionString => configuration.GetConnectionString("AlfaCentral");

    public Task<string?> GetServidorActivoAsync(CancellationToken ct = default)
        => LoggedAsync<string?>("GetServidorActivo", async () =>
        {
            var connectionString = ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                return null;

            await using var cn = new SqlConnection(connectionString);
            if (!await SchemaReadyAsync(cn, ct))
                return null;

            var valor = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT Valor FROM dbo.ConfiguracionCentral WHERE Clave = @Clave;",
                new { Clave }, cancellationToken: ct));
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        }, ct);

    public async Task<bool> DebeEjecutarWorkersAsync(CancellationToken ct = default)
    {
        var activo = await GetServidorActivoAsync(ct);
        return string.IsNullOrWhiteSpace(activo)
            || string.Equals(activo, NombreServidorLocal, StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetServidorActivoAsync(string? servidor, CancellationToken ct = default)
        => await LoggedAsync("SetServidorActivo", async () =>
        {
            EnsureAdmin();
            if (string.IsNullOrWhiteSpace(ConnectionString))
                throw new InvalidOperationException("No está configurada la conexión central.");

            var valorNormalizado = string.IsNullOrWhiteSpace(servidor) ? null : servidor.Trim();

            await using var cn = new SqlConnection(ConnectionString);
            await RequireSchemaAsync(cn, ct);

            await cn.ExecuteAsync(new CommandDefinition("""
                MERGE dbo.ConfiguracionCentral AS destino
                USING (SELECT @Clave AS Clave) AS origen ON destino.Clave = origen.Clave
                WHEN MATCHED THEN UPDATE SET Valor = @Valor, ModificadoUtc = GETUTCDATE(), ModificadoPor = @ModificadoPor
                WHEN NOT MATCHED THEN INSERT (Clave, Valor, ModificadoUtc, ModificadoPor)
                    VALUES (@Clave, @Valor, GETUTCDATE(), @ModificadoPor);
                """, new { Clave, Valor = valorNormalizado, ModificadoPor = user.CurrentUser?.UserName ?? string.Empty },
                cancellationToken: ct));

            await events.LogAuditAsync("Central", "SetWorkerServidorActivo", "ConfiguracionCentral", Clave,
                valorNormalizado is null
                    ? "Se quitó la restricción: todos los servidores corren los workers."
                    : $"Solo {valorNormalizado} corre los workers en segundo plano.",
                new { ServidorActivo = valorNormalizado }, ct);
            return true;
        }, ct);

    private void EnsureAdmin()
    {
        if (user.CurrentUser?.SuperAdmin != true)
            throw new InvalidOperationException("Solo un administrador central puede cambiar el servidor de workers.");
    }

    private static string ResolverNombreServidorLocal(IConfiguration configuration)
    {
        var configurado = configuration["AlfaCore:NombreServidor"];
        return string.IsNullOrWhiteSpace(configurado) ? Environment.MachineName : configurado.Trim();
    }

    private static Task<bool> SchemaReadyAsync(SqlConnection cn, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN OBJECT_ID('dbo.ConfiguracionCentral','U') IS NOT NULL THEN 1 ELSE 0 END AS bit);",
            cancellationToken: ct));

    private static async Task RequireSchemaAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await SchemaReadyAsync(cn, ct))
            throw new InvalidOperationException("Falta aplicar la actualización central de ConfiguracionCentral.");
    }

    private async Task<T> LoggedAsync<T>(string action, Func<Task<T>> operation, CancellationToken ct)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var message = ex is InvalidOperationException or ArgumentException ? ex.Message : "No se pudo consultar o guardar el servidor de workers.";
            var incident = await events.LogErrorAsync("Central", action, ex, message, ct: ct);
            throw new AppUserFacingException(message, incident, ex);
        }
    }
}
