using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

public sealed class ConversacionesRespuestasRapidasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IConversacionesRespuestasRapidasService
{
    private const string ModuleName = "Conversaciones";
    private const int LargoMaximoTexto = 4096; // límite de un mensaje de texto de WhatsApp

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<ConversacionRespuestaRapidaDto>> GetAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<ConversacionRespuestaRapidaDto>>("GetRespuestasRapidas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            // Si la base todavía no tiene la tabla (actualización pendiente) no hay respuestas: no es un error.
            var rows = await cn.QueryAsync<ConversacionRespuestaRapidaDto>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.CONV_RESPUESTAS_RAPIDAS', N'U') IS NOT NULL
                    SELECT IdRespuesta, LTRIM(RTRIM(Atajo)) AS Atajo, Texto
                    FROM dbo.CONV_RESPUESTAS_RAPIDAS
                    WHERE Activa = 1
                    ORDER BY Atajo;
                """, cancellationToken: token));
            return rows.AsList();
        }, "No se pudieron cargar las respuestas rápidas.", ct);

    public Task<ConversacionRespuestaRapidaDto> GuardarAsync(ConversacionRespuestaRapidaDto respuesta, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("GuardarRespuestaRapida", async token =>
        {
            ArgumentNullException.ThrowIfNull(respuesta);
            var atajo = ConversacionRespuestasRapidas.NormalizarAtajo(respuesta.Atajo);
            if (atajo.Length == 0)
                throw new InvalidOperationException("Escribí el atajo (por ejemplo: info).");
            var texto = (respuesta.Texto ?? string.Empty).Trim();
            if (texto.Length == 0)
                throw new InvalidOperationException("Escribí el texto de la respuesta.");
            if (texto.Length > LargoMaximoTexto)
                throw new InvalidOperationException($"El texto es muy largo: WhatsApp admite hasta {LargoMaximoTexto} caracteres.");

            await using var cn = new SqlConnection(ConnectionString);
            var guardada = await cn.QuerySingleOrDefaultAsync<ConversacionRespuestaRapidaDto>(new CommandDefinition("""
                IF EXISTS (
                    SELECT 1 FROM dbo.CONV_RESPUESTAS_RAPIDAS
                    WHERE Activa = 1
                      AND UPPER(LTRIM(RTRIM(Atajo))) = UPPER(@Atajo)
                      AND IdRespuesta <> @IdRespuesta)
                BEGIN
                    SELECT CAST(-1 AS int) AS IdRespuesta, @Atajo AS Atajo, N'' AS Texto;
                    RETURN;
                END;

                DECLARE @Id int = @IdRespuesta;
                IF @Id > 0
                    UPDATE dbo.CONV_RESPUESTAS_RAPIDAS
                    SET Atajo = @Atajo, Texto = @Texto, Usuario = @Usuario, FechaHora_Modificacion = GETDATE()
                    WHERE IdRespuesta = @Id AND Activa = 1;
                ELSE
                BEGIN
                    INSERT INTO dbo.CONV_RESPUESTAS_RAPIDAS (Atajo, Texto, Activa, Usuario, FechaHora_Grabacion)
                    VALUES (@Atajo, @Texto, 1, @Usuario, GETDATE());
                    SET @Id = CAST(SCOPE_IDENTITY() AS int);
                END;

                SELECT IdRespuesta, LTRIM(RTRIM(Atajo)) AS Atajo, Texto
                FROM dbo.CONV_RESPUESTAS_RAPIDAS
                WHERE IdRespuesta = @Id AND Activa = 1;
                """,
                new { IdRespuesta = Math.Max(0, respuesta.IdRespuesta), Atajo = atajo, Texto = texto, Usuario = Recortar(usuarioAccion, 60) },
                cancellationToken: token));

            if (guardada is null)
                throw new InvalidOperationException("La respuesta ya no existe. Recargá la lista.");
            if (guardada.IdRespuesta < 0)
                throw new InvalidOperationException($"Ya existe una respuesta con el atajo \"{atajo}\".");

            await appEvents.LogAuditAsync(
                ModuleName,
                "GuardarRespuestaRapida",
                "CONV_RESPUESTAS_RAPIDAS",
                guardada.IdRespuesta.ToString(CultureInfo.InvariantCulture),
                respuesta.IdRespuesta > 0 ? "Respuesta rápida modificada." : "Respuesta rápida creada.",
                new { guardada.IdRespuesta, guardada.Atajo, Usuario = usuarioAccion },
                token);
            return guardada;
        }, "No se pudo guardar la respuesta rápida.", ct);

    public Task EliminarAsync(int idRespuesta, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("EliminarRespuestaRapida", async token =>
        {
            if (idRespuesta <= 0)
                throw new InvalidOperationException("No se recibió la respuesta.");

            await using var cn = new SqlConnection(ConnectionString);
            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.CONV_RESPUESTAS_RAPIDAS
                SET Activa = 0, Usuario = @Usuario, FechaHora_Modificacion = GETDATE()
                WHERE IdRespuesta = @IdRespuesta AND Activa = 1;
                """, new { IdRespuesta = idRespuesta, Usuario = Recortar(usuarioAccion, 60) }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("La respuesta ya no existe.");

            await appEvents.LogAuditAsync(
                ModuleName,
                "EliminarRespuestaRapida",
                "CONV_RESPUESTAS_RAPIDAS",
                idRespuesta.ToString(CultureInfo.InvariantCulture),
                "Respuesta rápida eliminada.",
                new { IdRespuesta = idRespuesta, Usuario = usuarioAccion },
                token);
            return true;
        }, "No se pudo eliminar la respuesta rápida.", ct);

    private static string? Recortar(string? value, int largo)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length == 0 ? null : v.Length > largo ? v[..largo] : v;
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
}
