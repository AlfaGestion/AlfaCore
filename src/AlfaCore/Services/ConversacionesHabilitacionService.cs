using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Autorización central del worker de Conversaciones: nunca abre una conexión a las bases
/// de clientes. Mismo criterio que CentralCompraIaService (ver ese archivo para el diseño original) --
/// duplicado en vez de generalizado a propósito, para no tocar el flujo de Compra IA ya en producción.</summary>
public sealed class ConversacionesHabilitacionService(IConfiguration configuration, IAppUserSessionService user,
    IAppEventService events) : IConversacionesHabilitacionService
{
    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No está configurada la conexión central.");

    internal const string BasesHabilitadasSql = """
        SELECT b.id AS IdBase, b.idcliente AS IdCliente, ISNULL(b.nombre, '') AS Nombre,
            ISNULL(b.dbserver, '') AS DbServer, ISNULL(b.dbname, '') AS DbName,
            ISNULL(b.dbuser, '') AS DbUser, ISNULL(b.dbpassword, '') AS DbPassword
        FROM dbo.bases b
        WHERE b.ConversacionesWorkerHabilitado = 1
          AND EXISTS (
            SELECT 1 FROM dbo.ClienteModulos cm
            INNER JOIN dbo.Modulos m ON m.Id = cm.IdModulo
            WHERE LTRIM(RTRIM(cm.IdCliente)) = LTRIM(RTRIM(b.idcliente))
              AND UPPER(LTRIM(RTRIM(m.Codigo))) = @Codigo AND m.Activo = 1
              AND (UPPER(LTRIM(RTRIM(cm.Estado))) = 'ACTIVO'
                   OR (UPPER(LTRIM(RTRIM(cm.Estado))) = 'PRUEBA' AND cm.PruebaVenceUtc > GETUTCDATE()))
          )
        ORDER BY b.id;
        """;

    public Task<IReadOnlyList<BaseCentralDto>> GetBasesHabilitadasAsync(CancellationToken ct = default)
        => LoggedAsync("BasesHabilitadas", async () =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            // Antes de aplicar la actualización central no se autoriza ninguna conexión de cliente.
            if (!await SchemaReadyAsync(cn, ct)) return [];
            return (IReadOnlyList<BaseCentralDto>)(await cn.QueryAsync<BaseCentralDto>(new CommandDefinition(
                BasesHabilitadasSql, new { Codigo = ConversacionesHabilitacion.CodigoModulo }, cancellationToken: ct))).AsList();
        }, ct);

    public Task<IReadOnlySet<int>> GetBasesSeleccionadasAsync(CancellationToken ct = default)
        => LoggedAsync<IReadOnlySet<int>>("BasesSeleccionadas", async () =>
        {
            EnsureAdmin();
            await using var cn = new SqlConnection(ConnectionString);
            await RequireSchemaAsync(cn, ct);
            return (await cn.QueryAsync<int>(new CommandDefinition(
                "SELECT id FROM dbo.bases WHERE ConversacionesWorkerHabilitado = 1;", cancellationToken: ct))).ToHashSet();
        }, ct);

    public Task SetBaseSeleccionadaAsync(int idBase, string idCliente, bool seleccionada, CancellationToken ct = default)
        => LoggedAsync("SeleccionarBase", async () =>
        {
            EnsureAdmin();
            if (idBase <= 0 || string.IsNullOrWhiteSpace(idCliente)) throw new ArgumentException("Seleccioná una base válida.");
            await using var cn = new SqlConnection(ConnectionString);
            await RequireSchemaAsync(cn, ct);
            var changed = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.bases SET ConversacionesWorkerHabilitado = @Seleccionada
                WHERE id = @IdBase AND LTRIM(RTRIM(idcliente)) = @IdCliente;
                """, new { IdBase = idBase, IdCliente = idCliente.Trim(), Seleccionada = seleccionada }, cancellationToken: ct));
            if (changed != 1) throw new InvalidOperationException("La base ya no pertenece al cliente seleccionado. Actualizá el listado.");
            await events.LogAuditAsync("Central", "SeleccionarBaseConversaciones", "bases", idBase.ToString(),
                seleccionada ? "Base incluida en el job de Conversaciones." : "Base excluida del job de Conversaciones.",
                new { IdCliente = idCliente.Trim(), Seleccionada = seleccionada }, ct);
            return true;
        }, ct);

    private void EnsureAdmin()
    {
        if (user.CurrentUser?.SuperAdmin != true)
            throw new InvalidOperationException("Solo un administrador central puede seleccionar las bases para Conversaciones.");
    }

    private static Task<bool> SchemaReadyAsync(SqlConnection cn, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CAST(CASE WHEN COL_LENGTH('dbo.bases','ConversacionesWorkerHabilitado') IS NOT NULL
                AND OBJECT_ID('dbo.Modulos','U') IS NOT NULL AND OBJECT_ID('dbo.ClienteModulos','U') IS NOT NULL
                THEN 1 ELSE 0 END AS bit);
            """, cancellationToken: ct));

    private static async Task RequireSchemaAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await SchemaReadyAsync(cn, ct))
            throw new InvalidOperationException("Falta aplicar la actualización central de habilitación de Conversaciones.");
    }

    private async Task<T> LoggedAsync<T>(string action, Func<Task<T>> operation, CancellationToken ct)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var message = ex is InvalidOperationException or ArgumentException ? ex.Message : "No se pudo consultar o guardar la habilitación central de Conversaciones.";
            var incident = await events.LogErrorAsync("Central", action, ex, message, ct: ct);
            throw new AppUserFacingException(message, incident, ex);
        }
    }
}
