using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConversacionesService
{
    private async Task LoadQuotedMessagesAsync(SqlConnection cn, long conversationId, IReadOnlyList<ConversacionMensajeDto> messages, CancellationToken ct)
    {
        foreach (var message in messages)
            RestoreQuotedMessageReference(message);
        var ids = messages.Select(m => m.WhatsAppReplyToMessageId).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return;

        // Una consulta por página. Los originales no se agregan al timeline ni alteran sus cursores.
        const string sql = """
            SELECT m.IdMensaje, m.IdConversacion, m.TelefonoWhatsApp, m.WhatsAppMessageId,
                   m.MessageType, m.Direction, m.Texto, m.FechaHora, m.UsuarioAutor, m.SistemaAutor,
                   m.IdTecnicoAutor, ISNULL(t.Nombre, N'') AS TecnicoAutorNombre, m.Origen, m.AutorAutomatico,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.CONV_ADJUNTOS a WHERE a.IdMensaje = m.IdMensaje)
                        THEN 1 ELSE 0 END AS bit) AS TieneAdjuntos
            FROM dbo.CONV_MENSAJES m
            LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(m.IdTecnicoAutor))
            WHERE m.IdConversacion = @IdConversacion AND m.WhatsAppMessageId IN @Ids
              AND m.Direction IN (N'ENTRANTE', N'SALIENTE');
            """;
        try
        {
            var originals = await cn.QueryAsync<ConversacionMensajeDto>(new CommandDefinition(sql,
                new { IdConversacion = conversationId, Ids = ids }, cancellationToken: ct)).ConfigureAwait(false);
            AttachQuotedMessages(conversationId, messages, originals);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _appEvents.LogErrorAsync("Conversaciones", "LoadQuotedMessages", ex,
                "No se pudieron cargar los originales citados; la conversación sigue disponible.",
                new { IdConversacion = conversationId }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
    }

    internal static void RestoreQuotedMessageReference(ConversacionMensajeDto message)
    {
        if (!string.IsNullOrWhiteSpace(message.WhatsAppReplyToMessageId) || string.IsNullOrWhiteSpace(message.PayloadJson))
            return;
        try
        {
            using var doc = JsonDocument.Parse(message.PayloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;
            var actual = root.TryGetProperty("message", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object ? wrapped : root;
            message.WhatsAppReplyToMessageId = ExtractIncomingReplyToMessageId(actual, GetStringOrEmpty(actual, "type"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
    }

    internal static void AttachQuotedMessages(long conversationId, IReadOnlyList<ConversacionMensajeDto> messages, IEnumerable<ConversacionMensajeDto> originals)
    {
        var lookup = originals.Where(m => m.IdConversacion == conversationId && !string.IsNullOrWhiteSpace(m.WhatsAppMessageId)
                && m.Direction is "ENTRANTE" or "SALIENTE")
            .GroupBy(m => m.WhatsAppMessageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.IdMensaje).First(), StringComparer.Ordinal);
        foreach (var message in messages)
        {
            message.MensajeCitado = null;
            if (message.IdConversacion != conversationId || string.IsNullOrWhiteSpace(message.WhatsAppReplyToMessageId))
                continue;
            if (lookup.TryGetValue(message.WhatsAppReplyToMessageId, out var original) && original.IdMensaje != message.IdMensaje)
            {
                original.FechaHora = NormalizeStoredConversationTime(original.FechaHora);
                message.MensajeCitado = original;
            }
        }
    }
}
