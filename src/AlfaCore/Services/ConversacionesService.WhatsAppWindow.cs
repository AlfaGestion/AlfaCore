using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConversacionesService
{
    internal static string AddWhatsAppPhoneNumberIdToPayload(string payload, string phoneNumberId)
    {
        if (!IsValidMetaPhoneNumberId(phoneNumberId))
            return payload;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(payload) is System.Text.Json.Nodes.JsonObject root)
            {
                root["phone_number_id"] = phoneNumberId.Trim();
                return root.ToJsonString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Preservar el diagnóstico original si el proveedor no devolvió JSON.
        }
        return payload;
    }

    public Task<ConversacionWhatsAppVentanaDto> GetWhatsAppSendWindowAsync(
        long idConversacion, int? idNumeroWhatsApp = null, bool usarApiMetaPredeterminada = false,
        CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "VentanaWhatsApp", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(idConversacion, token);
            if (idNumeroWhatsApp is > 0)
                await conversacionesAuthorizationService.EnsureCanUseWhatsAppNumeroAsync(idNumeroWhatsApp.Value, token);
            var conversation = await RequireConversationAsync(idConversacion, token);
            EnsureRequestedWhatsAppNumeroMatchesConversation(idNumeroWhatsApp, conversation);
            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
            var numeroId = usarApiMetaPredeterminada ? null : idNumeroWhatsApp ?? conversation.IdNumeroWhatsApp;
            var numero = numeroId is > 0
                ? await conversacionesConfigService.GetWhatsAppNumeroAsync(numeroId.Value, token)
                : null;
            if (!usarApiMetaPredeterminada && IsUsableWhatsAppWebNumero(numero))
                return new ConversacionWhatsAppVentanaDto(true);
            var phoneNumberId = usarApiMetaPredeterminada
                ? config.PhoneNumberId
                : FirstValidMetaPhoneNumberId(numero?.PhoneNumberId, conversation.PhoneNumberId, config.PhoneNumberId);
            return await ReadWhatsAppSendWindowAsync(idConversacion, phoneNumberId, conversation.PhoneNumberId, conversation.TelefonoWhatsApp, token);
        }, "No se pudo verificar la ventana del número de WhatsApp seleccionado.", ct);

    private async Task<ConversacionWhatsAppVentanaDto> ReadWhatsAppSendWindowAsync(
        long idConversacion, string phoneNumberId, string conversationPhoneNumberId, string recipientPhone, CancellationToken ct)
    {
        if (!IsValidMetaPhoneNumberId(phoneNumberId))
            return new(false);

        // Sólo la actividad de las últimas 24 horas de este hilo puede habilitar un envío.
        // Se conserva el payload para distinguir el receptor real de Meta de mensajes QR/otra línea.
        const string sql = """
            SELECT IdMensaje, FechaHora, Direction, EstadoEnvio, PayloadJson, SistemaAutor, TelefonoWhatsApp
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @IdConversacion
              AND FechaHora > @Desde AND FechaHora <= @Ahora
              AND Direction IN (N'ENTRANTE', N'SALIENTE');
            """;
        var now = BusinessNow();
        await using var cn = new SqlConnection(ConnectionString);
        var messages = await cn.QueryAsync<WhatsAppWindowMessage>(new CommandDefinition(sql,
            new { IdConversacion = idConversacion, Desde = now.AddHours(-24), Ahora = now }, cancellationToken: ct));
        return EvaluateWhatsAppSendWindow(messages, phoneNumberId, conversationPhoneNumberId, now, recipientPhone);
    }

    internal static ConversacionWhatsAppVentanaDto EvaluateWhatsAppSendWindow(
        IEnumerable<WhatsAppWindowMessage> messages, string phoneNumberId, string conversationPhoneNumberId, DateTime now, string recipientPhone)
    {
        if (!IsValidMetaPhoneNumberId(phoneNumberId))
            return new(false);

        var scoped = messages.Where(m => m.FechaHora <= now && m.FechaHora > now.AddHours(-24)
            && WhatsAppWindowMessageMatchesNumber(m, phoneNumberId, conversationPhoneNumberId)
            && WhatsAppWindowMessageMatchesRecipient(m, recipientPhone)).ToList();
        var incoming = scoped.Where(m => m.Direction == "ENTRANTE")
            .OrderByDescending(m => m.FechaHora).ThenByDescending(m => m.IdMensaje).FirstOrDefault();
        if (incoming is null)
            return new(false);

        // Un rechazo de Meta prevalece hasta un nuevo mensaje del cliente a esta línea.
        // Incluso una plantilla entregada no abre por sí sola la ventana de texto libre.
        var rejected = scoped.Any(m => m.Direction == "SALIENTE"
                && (m.FechaHora > incoming.FechaHora || m.FechaHora == incoming.FechaHora && m.IdMensaje > incoming.IdMensaje)
                && m.EstadoEnvio == "ERROR_ENVIO"
                && WhatsAppOutboundErrorClassifier.ExtractGraphError(m.PayloadJson).Code == WhatsAppGraphErrorCodes.ReEngagementWindowExpired);
        return new(!rejected, incoming.FechaHora.AddHours(24), rejected);
    }

    private static bool WhatsAppWindowMessageMatchesNumber(WhatsAppWindowMessage message, string phoneNumberId, string conversationPhoneNumberId)
    {
        if (string.Equals(message.SistemaAutor, "WHATSAPP_WEB", StringComparison.OrdinalIgnoreCase))
            return false;
        var receivedPhoneNumberId = TryExtractPhoneNumberId(message.PayloadJson ?? string.Empty);
        // Compatibilidad con mensajes anteriores a metadata: sólo sirven para su línea asociada.
        return string.Equals(receivedPhoneNumberId.Length > 0 ? receivedPhoneNumberId : conversationPhoneNumberId.Trim(),
            phoneNumberId.Trim(), StringComparison.Ordinal);
    }

    private static bool WhatsAppWindowMessageMatchesRecipient(WhatsAppWindowMessage message, string recipientPhone)
    {
        var phone = message.TelefonoWhatsApp;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(message.PayloadJson ?? "{}");
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var payloadPhone = message.Direction == "ENTRANTE"
                    ? ReadNestedString(doc.RootElement, "message", "from")
                    : GetStringOrEmpty(doc.RootElement, "recipient_id");
                if (!string.IsNullOrWhiteSpace(payloadPhone))
                    phone = payloadPhone;
            }
        }
        catch (System.Text.Json.JsonException) { }
        // Misma equivalencia que el lookup existente (formato local/internacional), nunca por contacto.
        var expected = NormalizePhone(recipientPhone);
        var actual = NormalizePhone(phone);
        return expected.Length > 0 && (actual == expected
            || expected.Length >= 10 && actual.Length >= 10 && GetPhoneComparableTail(actual) == GetPhoneComparableTail(expected));
    }

    internal sealed class WhatsAppWindowMessage
    {
        public long IdMensaje { get; set; }
        public DateTime FechaHora { get; set; }
        public string Direction { get; set; } = string.Empty;
        public string EstadoEnvio { get; set; } = string.Empty;
        public string? PayloadJson { get; set; }
        public string? SistemaAutor { get; set; }
        public string TelefonoWhatsApp { get; set; } = string.Empty;
    }
}
