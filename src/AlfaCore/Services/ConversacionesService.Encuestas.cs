using System.Globalization;
using System.Text.Json;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Encuesta de satisfacción al cerrar una conversación de WhatsApp (2026-10-19).
///
/// Al cerrar (una persona, no los cierres automáticos), si está activa en Configuración → Automatización
/// y la ventana de 24 h sigue abierta, se manda "¿Cómo calificarías nuestra atención?" con tres botones
/// (Muy bien / Regular / Mal). La respuesta no reabre la conversación ni dispara al asistente: se guarda
/// en CONV_ENCUESTAS, se agradece y se ofrece dejar un comentario. Una calificación "Mal" avisa a quien
/// atendió (celular) y a los administradores (campana). Sólo números de WhatsApp oficial (API de Meta):
/// las sesiones por QR no admiten botones.
/// </summary>
public sealed partial class ConversacionesService
{
    /// <summary>Marca (CONV_MENSAJES.Origen) de los mensajes del cliente que responden la encuesta.</summary>
    internal const string OrigenRespuestaEncuesta = "ENCUESTA";

    private const int DiasEntreEncuestas = 7;
    private const int MinutosParaComentario = 30;

    private async Task TryEnviarEncuestaAlCerrarAsync(long idConversacion, string? usuarioCierre, string? idTecnicoAtendio, CancellationToken ct)
    {
        try
        {
            var config = await conversacionesConfigService.GetAutomatizacionesConfigAsync(ct).ConfigureAwait(false);
            if (!config.EncuestaActiva)
                return;

            var conversation = await RequireConversationAsync(idConversacion, ct).ConfigureAwait(false);
            if (!string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                return;

            // Sólo API de Meta: un número conectado por QR (WhatsApp Web) no puede mandar botones.
            var whatsAppConfig = await conversacionesConfigService.GetWhatsAppConfigAsync(ct).ConfigureAwait(false);
            var numero = conversation.IdNumeroWhatsApp is int idNumero
                ? await conversacionesConfigService.GetWhatsAppNumeroAsync(idNumero, ct).ConfigureAwait(false)
                : null;
            if (string.Equals(ResolveWhatsAppDeliveryProviderForNumero(whatsAppConfig, numero), ConversacionWhatsAppProviders.WhatsAppWeb, StringComparison.OrdinalIgnoreCase))
                return;

            if (!await IsWhatsAppWindowActiveAsync(idConversacion, ct).ConfigureAwait(false))
                return;

            var idEncuesta = await CrearEncuestaAsync(idConversacion, conversation.IdNumeroWhatsApp, usuarioCierre, idTecnicoAtendio, ct).ConfigureAwait(false);
            if (idEncuesta is not long id)
                return; // tabla todavía no creada o ya se mandó una esta semana

            var pregunta = string.IsNullOrWhiteSpace(config.EncuestaPregunta) ? ConversacionEncuestaTextos.PreguntaPorDefecto : config.EncuestaPregunta.Trim();
            try
            {
                var enviado = await SendMessageAsync(new ConversacionSendMessageRequest
                {
                    IdConversacion = idConversacion,
                    Texto = pregunta,
                    MessageType = "TEXT",
                    UsuarioAccion = "AlfaCore",
                    SistemaAccion = "ENCUESTA",
                    Botones = ConversacionEncuestaCalificaciones.Botones
                        .Select(b => new ConversacionBotonRespuesta(
                            $"{ConversacionEncuestaTextos.PrefijoBoton}{id.ToString(CultureInfo.InvariantCulture)}:{b.Valor.ToString(CultureInfo.InvariantCulture)}",
                            b.Titulo))
                        .ToList()
                }, ct).ConfigureAwait(false);

                await ActualizarEnvioEncuestaAsync(id, ConversacionEncuestaEstados.Enviada, enviado.IdMensaje, null, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ActualizarEnvioEncuestaAsync(id, ConversacionEncuestaEstados.Error, null, Truncar(ex.Message, 480), ct).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca frena el cierre de la conversación.
            await _appEvents.LogErrorAsync("Conversaciones", "EncuestaEnviar", ex,
                "No se pudo mandar la encuesta de satisfacción al cerrar la conversación.",
                new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
    }

    private async Task<long?> CrearEncuestaAsync(long idConversacion, int? idNumeroWhatsApp, string? usuarioCierre, string? idTecnicoAtendio, CancellationToken ct)
    {
        // Atendido por el asistente: en las últimas 48 h salieron respuestas del asistente y ninguna de
        // una persona (para separar en el informe "atendido por el asistente" de "por una persona").
        var sql = $"""
            IF OBJECT_ID(N'dbo.CONV_ENCUESTAS', N'U') IS NULL
            BEGIN
                SELECT CAST(NULL AS bigint);
                RETURN;
            END;

            IF EXISTS (SELECT 1 FROM dbo.CONV_ENCUESTAS
                       WHERE IdConversacion = @IdConversacion
                         AND Estado <> N'ERROR'
                         AND FechaEnvio >= DATEADD(day, -@Dias, GETDATE()))
            BEGIN
                SELECT CAST(NULL AS bigint);
                RETURN;
            END;

            DECLARE @Desde datetime = DATEADD(hour, -48, GETDATE());
            DECLARE @Humano bit = CASE WHEN EXISTS (
                SELECT 1 FROM dbo.CONV_MENSAJES m
                WHERE m.IdConversacion = @IdConversacion AND m.Direction = N'SALIENTE' AND m.FechaHora >= @Desde
                  AND (NULLIF(LTRIM(RTRIM(ISNULL(m.UsuarioAutor, N''))), N'') IS NOT NULL OR ISNULL(m.Origen, N'') = N'WHATSAPP_BUSINESS_APP')
                  AND {AutorMensajeSql("m")} NOT IN ({AutomatedOutgoingSistemaAutorSqlList})) THEN 1 ELSE 0 END;
            DECLARE @Asistente bit = CASE WHEN @Humano = 0 AND EXISTS (
                SELECT 1 FROM dbo.CONV_MENSAJES m
                WHERE m.IdConversacion = @IdConversacion AND m.Direction = N'SALIENTE' AND m.FechaHora >= @Desde
                  AND {AutorMensajeSql("m")} = N'BOT') THEN 1 ELSE 0 END;

            INSERT INTO dbo.CONV_ENCUESTAS (IdConversacion, IdNumeroWhatsApp, Canal, Estado, FechaEnvio, UsuarioCierre, IdTecnicoAtendio, AtendidoPorAsistente)
            VALUES (@IdConversacion, @IdNumero, N'WHATSAPP', N'ENVIANDO', GETDATE(), @Usuario, @Tecnico, @Asistente);

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await EnsureMensajeOrigenColumnAsync(cn, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@Dias", DiasEntreEncuestas);
        cmd.Parameters.AddWithValue("@IdNumero", (object?)idNumeroWhatsApp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Usuario", DbNullable(Truncar((usuarioCierre ?? string.Empty).Trim(), 50)));
        cmd.Parameters.AddWithValue("@Tecnico", DbNullable(NormalizeTechnicianId(idTecnicoAtendio)));
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task ActualizarEnvioEncuestaAsync(long idEncuesta, string estado, long? idMensaje, string? error, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            UPDATE dbo.CONV_ENCUESTAS SET Estado = @Estado, IdMensaje = ISNULL(@IdMensaje, IdMensaje), Error = @Error
            WHERE IdEncuesta = @Id;
            """, cn);
        cmd.Parameters.AddWithValue("@Id", idEncuesta);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@IdMensaje", (object?)idMensaje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Error", DbNullable(error));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Id del botón tocado en un mensaje interactivo de WhatsApp ("csat:123:3"), o vacío. El RawJson del
    /// mensaje entrante trae interactive.button_reply.id.
    /// </summary>
    internal static string ExtraerIdBotonRespuesta(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || !rawJson.Contains("button_reply", StringComparison.Ordinal))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return BuscarIdBoton(doc.RootElement) ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        static string? BuscarIdBoton(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("button_reply", out var reply) && reply.ValueKind == JsonValueKind.Object
                    && reply.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    return id.GetString();
                foreach (var prop in el.EnumerateObject())
                {
                    var found = BuscarIdBoton(prop.Value);
                    if (found is not null)
                        return found;
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    var found = BuscarIdBoton(item);
                    if (found is not null)
                        return found;
                }
            }

            return null;
        }
    }

    /// <summary>"csat:123:3" → (123, 3); null si no es un botón de la encuesta.</summary>
    internal static (long IdEncuesta, int Calificacion)? ParsearBotonEncuesta(string? idBoton)
    {
        if (string.IsNullOrWhiteSpace(idBoton) || !idBoton.StartsWith(ConversacionEncuestaTextos.PrefijoBoton, StringComparison.Ordinal))
            return null;

        var partes = idBoton[ConversacionEncuestaTextos.PrefijoBoton.Length..].Split(':');
        if (partes.Length != 2
            || !long.TryParse(partes[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEncuesta)
            || !int.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var calificacion)
            || calificacion is < ConversacionEncuestaCalificaciones.Mal or > ConversacionEncuestaCalificaciones.MuyBien)
            return null;

        return (idEncuesta, calificacion);
    }

    /// <summary>
    /// Mensaje entrante de WhatsApp: si es la respuesta a la encuesta (un botón, o el comentario escrito
    /// poco después de calificar) se procesa acá y devuelve true: el que llama no reabre la conversación
    /// ni corre las respuestas automáticas.
    /// </summary>
    private async Task<bool> TryProcesarRespuestaEncuestaAsync(long idConversacion, long idMensaje, IncomingWhatsAppMessage incoming, CancellationToken ct)
    {
        try
        {
            if (ParsearBotonEncuesta(ExtraerIdBotonRespuesta(incoming.RawJson)) is { } boton)
                return await RegistrarCalificacionAsync(idConversacion, idMensaje, boton.IdEncuesta, boton.Calificacion, ct).ConfigureAwait(false);

            if (!string.Equals(incoming.MessageType, "TEXT", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(incoming.Text))
                return false;

            return await TryRegistrarComentarioAsync(idConversacion, idMensaje, incoming.Text, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _appEvents.LogErrorAsync("Conversaciones", "EncuestaRespuesta", ex,
                "No se pudo procesar la respuesta a la encuesta; el mensaje siguió el camino normal.",
                new { idConversacion, idMensaje }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<bool> RegistrarCalificacionAsync(long idConversacion, long idMensaje, long idEncuesta, int calificacion, CancellationToken ct)
    {
        string? idTecnicoAtendio = null;
        int filas;
        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand("""
                IF OBJECT_ID(N'dbo.CONV_ENCUESTAS', N'U') IS NULL
                BEGIN
                    SELECT CAST(-1 AS int), CAST(NULL AS nvarchar(20));
                    RETURN;
                END;
                UPDATE dbo.CONV_ENCUESTAS
                SET Calificacion = @Calificacion, FechaRespuesta = GETDATE(), Estado = N'RESPONDIDA', EsperandoComentario = 1
                WHERE IdEncuesta = @IdEncuesta AND IdConversacion = @IdConversacion AND Calificacion IS NULL;
                SELECT @@ROWCOUNT, (SELECT IdTecnicoAtendio FROM dbo.CONV_ENCUESTAS WHERE IdEncuesta = @IdEncuesta);
                """, cn);
            cmd.Parameters.AddWithValue("@IdEncuesta", idEncuesta);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            cmd.Parameters.AddWithValue("@Calificacion", calificacion);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            filas = rd.GetInt32(0);
            idTecnicoAtendio = rd.IsDBNull(1) ? null : rd.GetString(1);
        }

        if (filas < 0)
            return false;

        await MarcarMensajeComoRespuestaEncuestaAsync(idMensaje, ct).ConfigureAwait(false);
        if (filas == 0)
            return true; // ya había respondido (doble toque): no se repite el agradecimiento

        await SendMessageAsync(new ConversacionSendMessageRequest
        {
            IdConversacion = idConversacion,
            Texto = calificacion == ConversacionEncuestaCalificaciones.MuyBien
                ? ConversacionEncuestaTextos.AgradecimientoBueno
                : ConversacionEncuestaTextos.AgradecimientoMejorable,
            MessageType = "TEXT",
            UsuarioAccion = "AlfaCore",
            SistemaAccion = "ENCUESTA"
        }, ct).ConfigureAwait(false);

        await AddInternalEventCoreAsync(idConversacion,
            $"⭐ El cliente calificó la atención: {ConversacionEncuestaCalificaciones.Emoji(calificacion)} {ConversacionEncuestaCalificaciones.Etiqueta(calificacion)}.",
            null, null, "AlfaCore", "ENCUESTA", ct).ConfigureAwait(false);

        if (calificacion == ConversacionEncuestaCalificaciones.Mal)
            await AvisarCalificacionMalaAsync(idConversacion, idTecnicoAtendio, ct).ConfigureAwait(false);

        await _appEvents.LogAuditAsync("Conversaciones", "EncuestaCalificacion", "CONV_ENCUESTAS",
            idEncuesta.ToString(CultureInfo.InvariantCulture), "El cliente respondió la encuesta de satisfacción.",
            new { idConversacion, calificacion }, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Texto que llega poco después de calificar (y sin pregunta): es el comentario de la encuesta. Si trae
    /// una pregunta es una consulta nueva y sigue el camino normal.
    /// </summary>
    private async Task<bool> TryRegistrarComentarioAsync(long idConversacion, long idMensaje, string texto, CancellationToken ct)
    {
        var esConsulta = texto.Contains('?') || texto.Contains('¿');
        long? idEncuesta = null;
        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand("""
                IF OBJECT_ID(N'dbo.CONV_ENCUESTAS', N'U') IS NULL
                BEGIN
                    SELECT CAST(NULL AS bigint);
                    RETURN;
                END;
                DECLARE @Id bigint = (
                    SELECT TOP (1) IdEncuesta FROM dbo.CONV_ENCUESTAS
                    WHERE IdConversacion = @IdConversacion AND EsperandoComentario = 1
                    ORDER BY IdEncuesta DESC);
                IF @Id IS NULL
                BEGIN
                    SELECT CAST(NULL AS bigint);
                    RETURN;
                END;
                IF @EsConsulta = 1 OR NOT EXISTS (SELECT 1 FROM dbo.CONV_ENCUESTAS
                                                  WHERE IdEncuesta = @Id AND FechaRespuesta >= DATEADD(minute, -@Minutos, GETDATE()))
                BEGIN
                    UPDATE dbo.CONV_ENCUESTAS SET EsperandoComentario = 0 WHERE IdEncuesta = @Id;
                    SELECT CAST(NULL AS bigint);
                    RETURN;
                END;
                UPDATE dbo.CONV_ENCUESTAS
                SET Comentario = @Comentario, FechaComentario = GETDATE(), EsperandoComentario = 0
                WHERE IdEncuesta = @Id;
                SELECT @Id;
                """, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            cmd.Parameters.AddWithValue("@EsConsulta", esConsulta);
            cmd.Parameters.AddWithValue("@Minutos", MinutosParaComentario);
            cmd.Parameters.AddWithValue("@Comentario", Truncar(texto.Trim(), 1000));
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            idEncuesta = result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        if (idEncuesta is null)
            return false;

        await MarcarMensajeComoRespuestaEncuestaAsync(idMensaje, ct).ConfigureAwait(false);
        await SendMessageAsync(new ConversacionSendMessageRequest
        {
            IdConversacion = idConversacion,
            Texto = ConversacionEncuestaTextos.AgradecimientoComentario,
            MessageType = "TEXT",
            UsuarioAccion = "AlfaCore",
            SistemaAccion = "ENCUESTA"
        }, ct).ConfigureAwait(false);
        await AddInternalEventCoreAsync(idConversacion,
            $"💬 Comentario de la encuesta: \"{Truncar(texto.Trim(), 300)}\"",
            null, null, "AlfaCore", "ENCUESTA", ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Marca el mensaje del cliente como respuesta de encuesta: el mantenimiento no reabre por él.</summary>
    private async Task MarcarMensajeComoRespuestaEncuestaAsync(long idMensaje, CancellationToken ct)
    {
        if (idMensaje <= 0)
            return;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await EnsureMensajeOrigenColumnAsync(cn, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("UPDATE dbo.CONV_MENSAJES SET Origen = @Origen WHERE IdMensaje = @Id;", cn);
        cmd.Parameters.AddWithValue("@Origen", OrigenRespuestaEncuesta);
        cmd.Parameters.AddWithValue("@Id", idMensaje);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Calificación "Mal": aviso al celular de quien atendió (la campana la muestra también a los administradores).</summary>
    private async Task AvisarCalificacionMalaAsync(long idConversacion, string? idTecnicoAtendio, CancellationToken ct)
    {
        var idTecnico = string.IsNullOrWhiteSpace(idTecnicoAtendio) ? null : await ResolveTechnicianIdOrNullAsync(idTecnicoAtendio, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(idTecnico)
            || serviceProvider?.GetService(typeof(IAvisosPushNotifier)) is not IAvisosPushNotifier avisosPush)
            return;

        try
        {
            await avisosPush.NotificarTecnicoAsync(
                idTecnico,
                "Calificación mala",
                "Un cliente calificó la atención como «Mal». Revisá la conversación.",
                $"/conversaciones?id={idConversacion.ToString(CultureInfo.InvariantCulture)}",
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _appEvents.LogErrorAsync("Conversaciones", "EncuestaAvisoMala", ex,
                "No se pudo avisar al celular la calificación mala.", new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
    }
}
