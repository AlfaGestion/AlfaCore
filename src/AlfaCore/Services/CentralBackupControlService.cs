using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CentralBackupControlService(IConfiguration configuration, IAppEventService appEvents) : ICentralBackupControlService
{
    private static readonly string[] ResultadosValidos = ["OK", "ERROR", "ADVERTENCIA"];
    private static readonly string[] TiposValidos = ["COMPLETO", "DIFERENCIAL"];

    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<long> RegistrarAsync(BackupStatusRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var idCliente = request.IdCliente.Trim();
        var dbServidor = request.DbServidor.Trim();
        var dbNombre = request.DbNombre.Trim();
        var tipoBackup = request.TipoBackup.Trim().ToUpperInvariant();
        var resultado = request.Resultado.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(idCliente))
            throw new InvalidOperationException("Falta el IdCliente.");
        if (string.IsNullOrWhiteSpace(dbServidor) || string.IsNullOrWhiteSpace(dbNombre))
            throw new InvalidOperationException("Faltan DbServidor y/o DbNombre.");
        if (!TiposValidos.Contains(tipoBackup))
            throw new InvalidOperationException("TipoBackup debe ser COMPLETO o DIFERENCIAL.");
        if (!ResultadosValidos.Contains(resultado))
            throw new InvalidOperationException("Resultado debe ser OK, ERROR o ADVERTENCIA.");

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);

            var idBase = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                SELECT TOP (1) id
                FROM dbo.bases
                WHERE idcliente = @IdCliente
                  AND UPPER(LTRIM(RTRIM(dbserver))) = UPPER(@DbServidor)
                  AND UPPER(LTRIM(RTRIM(dbname))) = UPPER(@DbNombre);
                """,
                new { IdCliente = idCliente, DbServidor = dbServidor, DbNombre = dbNombre },
                cancellationToken: ct)).ConfigureAwait(false);

            const string insertSql = """
                INSERT INTO dbo.ALFACORE_BACKUPS_CONTROL
                (
                    IdCliente, IdBase, DbServidor, DbNombre, TipoBackup, Resultado, Mensaje,
                    HostCliente, UsuarioSql, InstanciaSql, VersionSql, SistemaOperativo,
                    TamanioBaseMB, EspacioLibreDiscoGB, EspacioTotalDiscoGB,
                    EspacioLibreDiscoBckGB, EspacioTotalDiscoBckGB, FechaHoraBackup
                )
                OUTPUT INSERTED.IdControl
                VALUES
                (
                    @IdCliente, @IdBase, @DbServidor, @DbNombre, @TipoBackup, @Resultado, @Mensaje,
                    @HostCliente, @UsuarioSql, @InstanciaSql, @VersionSql, @SistemaOperativo,
                    @TamanioBaseMB, @EspacioLibreDiscoGB, @EspacioTotalDiscoGB,
                    @EspacioLibreDiscoBckGB, @EspacioTotalDiscoBckGB, @FechaHoraBackup
                );
                """;

            var idControl = await cn.QuerySingleAsync<long>(new CommandDefinition(insertSql, new
            {
                IdCliente = idCliente,
                IdBase = idBase,
                DbServidor = dbServidor,
                DbNombre = dbNombre,
                TipoBackup = tipoBackup,
                Resultado = resultado,
                request.Mensaje,
                request.HostCliente,
                request.UsuarioSql,
                request.InstanciaSql,
                request.VersionSql,
                request.SistemaOperativo,
                request.TamanioBaseMB,
                request.EspacioLibreDiscoGB,
                request.EspacioTotalDiscoGB,
                request.EspacioLibreDiscoBckGB,
                request.EspacioTotalDiscoBckGB,
                request.FechaHoraBackup
            }, cancellationToken: ct)).ConfigureAwait(false);

            return idControl;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync("BackupsControl", "Registrar", ex, "No se pudo registrar el estado del backup.", new { request.IdCliente, request.DbServidor, request.DbNombre }, AppEventSeverity.Error, ct);
            throw;
        }
    }
}
