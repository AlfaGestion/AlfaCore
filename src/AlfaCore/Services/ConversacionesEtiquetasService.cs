using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class ConversacionesEtiquetasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IConversacionesEtiquetasService
{
    private const string ModuleName = "Conversaciones";
    private const string ColorPorDefecto = "#64748B";
    private const int LargoMaximoNombre = 60;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<ConversacionEtiquetaDto>> GetEtiquetasAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<ConversacionEtiquetaDto>>("GetEtiquetas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var rows = await cn.QueryAsync<ConversacionEtiquetaDto>(new CommandDefinition("""
                SELECT IdEtiqueta, LTRIM(RTRIM(Nombre)) AS Nombre, ISNULL(Color, '') AS Color
                FROM dbo.CONV_ETIQUETAS
                WHERE ISNULL(Activa, 1) = 1
                ORDER BY Nombre;
                """, cancellationToken: token));
            return rows.AsList();
        }, "No se pudieron cargar las etiquetas de conversaciones.", ct);

    public Task<IReadOnlyDictionary<long, IReadOnlyList<ConversacionEtiquetaDto>>> GetEtiquetasPorConversacionAsync(IReadOnlyCollection<long> idsConversacion, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyDictionary<long, IReadOnlyList<ConversacionEtiquetaDto>>>("GetEtiquetasPorConversacion", async token =>
        {
            var ids = idsConversacion.Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<long, IReadOnlyList<ConversacionEtiquetaDto>>();

            await using var cn = new SqlConnection(ConnectionString);
            var rows = await cn.QueryAsync<EtiquetaDeConversacionRow>(new CommandDefinition("""
                SELECT ce.IdConversacion, e.IdEtiqueta, LTRIM(RTRIM(e.Nombre)) AS Nombre, ISNULL(e.Color, '') AS Color
                FROM dbo.CONV_CONVERSACION_ETIQUETAS ce
                INNER JOIN dbo.CONV_ETIQUETAS e ON e.IdEtiqueta = ce.IdEtiqueta
                WHERE ce.IdConversacion IN @Ids
                  AND ISNULL(e.Activa, 1) = 1
                ORDER BY e.Nombre;
                """, new { Ids = ids }, cancellationToken: token));
            return rows
                .GroupBy(r => r.IdConversacion)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<ConversacionEtiquetaDto>)g
                        .Select(r => new ConversacionEtiquetaDto { IdEtiqueta = r.IdEtiqueta, Nombre = r.Nombre, Color = r.Color })
                        .ToList());
        }, "No se pudieron cargar las etiquetas de las conversaciones.", ct);

    public Task SetEtiquetasConversacionAsync(long idConversacion, IReadOnlyCollection<int> idsEtiqueta, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("SetEtiquetasConversacion", async token =>
        {
            if (idConversacion <= 0)
                throw new InvalidOperationException("No se recibió la conversación.");
            var ids = idsEtiqueta.Where(x => x > 0).Distinct().ToList();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            // "IN @Ids" con una lista vacía no es SQL válido: sin etiquetas se pasa un id imposible y se borran todas.
            await cn.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.CONV_CONVERSACION_ETIQUETAS
                WHERE IdConversacion = @IdConversacion
                  AND IdEtiqueta NOT IN @Ids;

                INSERT INTO dbo.CONV_CONVERSACION_ETIQUETAS (IdConversacion, IdEtiqueta, FechaHora_Grabacion)
                SELECT @IdConversacion, e.IdEtiqueta, GETDATE()
                FROM dbo.CONV_ETIQUETAS e
                WHERE e.IdEtiqueta IN @Ids
                  AND ISNULL(e.Activa, 1) = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.CONV_CONVERSACION_ETIQUETAS x
                      WHERE x.IdConversacion = @IdConversacion AND x.IdEtiqueta = e.IdEtiqueta);
                """,
                new { IdConversacion = idConversacion, Ids = ids.Count > 0 ? ids : [-1] },
                (SqlTransaction)tx,
                cancellationToken: token));
            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SetEtiquetasConversacion",
                "CONV_CONVERSACIONES",
                idConversacion.ToString(CultureInfo.InvariantCulture),
                "Etiquetas de la conversación actualizadas.",
                new { IdConversacion = idConversacion, Etiquetas = ids, Usuario = usuarioAccion },
                token);
            return true;
        }, "No se pudieron guardar las etiquetas de la conversación.", ct);

    public Task<ConversacionEtiquetaDto> CrearEtiquetaAsync(string nombre, string? color, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("CrearEtiqueta", async token =>
        {
            var name = Regex.Replace(nombre ?? string.Empty, @"\s+", " ").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Escribí el nombre de la etiqueta.");
            if (name.Length > LargoMaximoNombre)
                name = name[..LargoMaximoNombre];
            var hex = NormalizarColor(color);

            await using var cn = new SqlConnection(ConnectionString);
            var etiqueta = await cn.QuerySingleAsync<ConversacionEtiquetaDto>(new CommandDefinition("""
                DECLARE @IdEtiqueta int;

                SELECT @IdEtiqueta = MIN(IdEtiqueta)
                FROM dbo.CONV_ETIQUETAS
                WHERE UPPER(LTRIM(RTRIM(Nombre))) = UPPER(@Nombre);

                IF @IdEtiqueta IS NULL
                BEGIN
                    INSERT INTO dbo.CONV_ETIQUETAS (Nombre, Color, Activa, FechaHora_Grabacion)
                    VALUES (@Nombre, @Color, 1, GETDATE());
                    SET @IdEtiqueta = CAST(SCOPE_IDENTITY() AS int);
                END
                ELSE
                    UPDATE dbo.CONV_ETIQUETAS
                    SET Activa = 1, FechaHora_Modificacion = GETDATE()
                    WHERE IdEtiqueta = @IdEtiqueta AND ISNULL(Activa, 1) = 0;

                SELECT IdEtiqueta, LTRIM(RTRIM(Nombre)) AS Nombre, ISNULL(Color, '') AS Color
                FROM dbo.CONV_ETIQUETAS
                WHERE IdEtiqueta = @IdEtiqueta;
                """, new { Nombre = name, Color = hex }, cancellationToken: token));

            await appEvents.LogAuditAsync(
                ModuleName,
                "CrearEtiqueta",
                "CONV_ETIQUETAS",
                etiqueta.IdEtiqueta.ToString(CultureInfo.InvariantCulture),
                "Etiqueta de conversación creada.",
                new { etiqueta.IdEtiqueta, etiqueta.Nombre, Usuario = usuarioAccion },
                token);
            return etiqueta;
        }, "No se pudo crear la etiqueta.", ct);

    public Task DesactivarEtiquetaAsync(int idEtiqueta, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("DesactivarEtiqueta", async token =>
        {
            if (idEtiqueta <= 0)
                throw new InvalidOperationException("No se recibió la etiqueta.");

            await using var cn = new SqlConnection(ConnectionString);
            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.CONV_ETIQUETAS
                SET Activa = 0, FechaHora_Modificacion = GETDATE()
                WHERE IdEtiqueta = @IdEtiqueta;
                """, new { IdEtiqueta = idEtiqueta }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("La etiqueta ya no existe.");

            await appEvents.LogAuditAsync(
                ModuleName,
                "DesactivarEtiqueta",
                "CONV_ETIQUETAS",
                idEtiqueta.ToString(CultureInfo.InvariantCulture),
                "Etiqueta de conversación desactivada.",
                new { IdEtiqueta = idEtiqueta, Usuario = usuarioAccion },
                token);
            return true;
        }, "No se pudo eliminar la etiqueta.", ct);

    private static string NormalizarColor(string? color)
    {
        var value = (color ?? string.Empty).Trim();
        return Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$") ? value.ToUpperInvariant() : ColorPorDefecto;
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private sealed class EtiquetaDeConversacionRow
    {
        public long IdConversacion { get; set; }
        public int IdEtiqueta { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }
}
