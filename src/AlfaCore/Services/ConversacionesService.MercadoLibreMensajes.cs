using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Mercado Libre: mensajes de postventa (tópico "messages") y llamadas a la API con renovación del
/// acceso cuando vence (Mercado Libre lo da por 6 horas). Las preguntas de publicaciones siguen en
/// ConversacionesService.cs; una venta es una conversación con IdentificadorExternoConversacion = "pack-{id}".
/// </summary>
public sealed partial class ConversacionesService
{
    internal const string MercadoLibrePackPrefix = "pack-";
    /// <summary>Mercado Libre corta los mensajes de postventa en 350 caracteres.</summary>
    internal const int MercadoLibreMensajeMaximo = 350;

    internal static bool IsMercadoLibrePackConversation(string? identificadorExterno)
        => (identificadorExterno ?? string.Empty).StartsWith(MercadoLibrePackPrefix, StringComparison.OrdinalIgnoreCase);

    private static string MercadoLibreApiBase(ConversacionMercadoLibreConfigDto config)
        => string.IsNullOrWhiteSpace(config.ApiBaseUrl) ? "https://api.mercadolibre.com" : config.ApiBaseUrl.Trim().TrimEnd('/');

    /// <summary>
    /// Manda un pedido a la API de Mercado Libre con el token de la base. Si responde 401 (acceso vencido),
    /// renueva el acceso una vez y reintenta. Devuelve el código y el cuerpo.
    /// </summary>
    private async Task<(HttpStatusCode Status, string Body)> SendMercadoLibreApiAsync(
        ConversacionMercadoLibreConfigDto config,
        Func<HttpRequestMessage> buildRequest,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = MetaSendTimeout;
        for (var intento = 0; ; intento++)
        {
            using var request = buildRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized || intento > 0 || string.IsNullOrWhiteSpace(config.RefreshToken))
                return (response.StatusCode, body);

            var renovado = await RefreshMercadoLibreAccessTokenAsync(config, [], ct);
            config.AccessToken = renovado.AccessToken;
            config.RefreshToken = renovado.RefreshToken;
        }
    }

    /// <summary>Procesa una notificación del tópico "messages" (postventa). Devuelve true si grabó algo.</summary>
    private async Task<bool> ProcesarMensajeMercadoLibreAsync(
        ConversacionMercadoLibreConfigDto? config,
        MercadoLibreNotification notification,
        CancellationToken ct)
    {
        if (config is null || !config.IsConfiguredForApi)
            return false;

        var messageId = ExtractMercadoLibreMessageId(notification.Resource);
        if (string.IsNullOrWhiteSpace(messageId))
            return false;

        var (status, body) = await SendMercadoLibreApiAsync(config, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{MercadoLibreApiBase(config)}/messages/{Uri.EscapeDataString(messageId)}?tag=post_sale");
            request.Headers.TryAddWithoutValidation("x-format-new", "true");
            return request;
        }, ct);
        if (!IsSuccess(status))
            throw new InvalidOperationException($"Mercado Libre devolvió {(int)status} al leer el mensaje de postventa {messageId}.");

        var mensaje = ParseMercadoLibrePostVenta(body, messageId);
        if (mensaje is null || string.IsNullOrWhiteSpace(mensaje.PackId))
            return false;

        // Lo que manda el propio vendedor (desde Mercado Libre o desde AlfaCore) no es un mensaje entrante.
        if (string.Equals(mensaje.FromUserId, config.SellerId?.Trim(), StringComparison.Ordinal))
            return false;

        var comprador = await TryGetMercadoLibreBuyerAsync(config, mensaje.FromUserId, ct);
        var nombre = FirstNonEmpty(comprador?.Nickname, string.IsNullOrWhiteSpace(mensaje.FromUserId) ? string.Empty : $"Comprador {mensaje.FromUserId}");
        var conversationId = await EnsureMercadoLibrePackConversationAsync(mensaje, nombre, ct);

        var incoming = new IncomingInstagramMessage
        {
            AccountId = config.SellerId?.Trim() ?? string.Empty,
            SenderId = mensaje.FromUserId,
            RecipientId = config.SellerId?.Trim() ?? string.Empty,
            MessageId = $"meli-msg-{mensaje.MessageId}",
            MessageType = "TEXT",
            Timestamp = mensaje.Fecha == default ? BusinessNow() : mensaje.Fecha,
            Text = mensaje.Texto,
            RawJson = JsonSerializer.Serialize(new
            {
                provider = "mercadolibre",
                kind = "post_sale",
                message_id = mensaje.MessageId,
                pack_id = mensaje.PackId,
                buyer_id = mensaje.FromUserId,
                buyer_nickname = comprador?.Nickname,
                seller_id = config.SellerId,
                text = mensaje.Texto,
                attachments = mensaje.Adjuntos
            })
        };

        var stored = await InsertInstagramMessageIfMissingAsync(conversationId, incoming, ct);
        await RefreshConversationAsync(conversationId, incoming.Timestamp, incoming.Text, CancellationToken.None, reopenIfClosed: true);
        if (stored.Created)
        {
            await NotifyIncomingMessageAsync(conversationId, stored.MessageId, CancellationToken.None);
            // Mismo criterio que el resto de los canales: token propio, no el del webhook.
            if (!await TryAutoReplyReglasAsync(conversationId, incoming.Text, CancellationToken.None))
            {
                await TryAutoReplyWelcomeAsync(conversationId, CancellationToken.None);
                await TryAutoReplyOutOfHoursAsync(conversationId, CancellationToken.None);
                await TryAutoReplyBotAsync(conversationId, incoming.Text, CancellationToken.None);
            }
        }

        return true;
    }

    private async Task<long> EnsureMercadoLibrePackConversationAsync(MercadoLibrePostVenta mensaje, string nombre, CancellationToken ct)
    {
        var clave = MercadoLibrePackPrefix + mensaje.PackId;
        const string sql = """
            DECLARE @Id bigint = (
                SELECT TOP (1) IdConversacion
                FROM dbo.CONV_CONVERSACIONES WITH (UPDLOCK, HOLDLOCK)
                WHERE Canal = N'MERCADOLIBRE' AND IdentificadorExternoConversacion = @Clave
                ORDER BY IdConversacion DESC);

            IF @Id IS NULL
            BEGIN
                INSERT INTO dbo.CONV_CONVERSACIONES
                    (Canal, NombreVisible, TelefonoWhatsApp, IdentificadorExternoContacto, IdentificadorExternoConversacion,
                     CodigoEstado, ResumenUltimoMensaje, FechaHoraPrimerMensaje, FechaHoraUltimoMensaje, FechaHora_Grabacion)
                VALUES
                    (N'MERCADOLIBRE', @Nombre, @Detalle, @BuyerId, @Clave,
                     N'ABIERTA', @Resumen, @FechaHora, @FechaHora, GETDATE());
                SET @Id = CAST(SCOPE_IDENTITY() AS bigint);
            END
            ELSE
            BEGIN
                UPDATE dbo.CONV_CONVERSACIONES
                SET NombreVisible = COALESCE(NULLIF(@Nombre, N''), NombreVisible),
                    IdentificadorExternoContacto = COALESCE(NULLIF(@BuyerId, N''), IdentificadorExternoContacto),
                    Archivada = 0,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdConversacion = @Id;
            END

            SELECT @Id;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@Clave", clave);
        cmd.Parameters.AddWithValue("@Nombre", DbNullable(nombre));
        cmd.Parameters.AddWithValue("@Detalle", DbNullable($"Venta {mensaje.PackId}"));
        cmd.Parameters.AddWithValue("@BuyerId", DbNullable(mensaje.FromUserId));
        cmd.Parameters.AddWithValue("@Resumen", DbNullable(TrimForSummary(mensaje.Texto)));
        cmd.Parameters.AddWithValue("@FechaHora", mensaje.Fecha == default ? BusinessNow() : mensaje.Fecha);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await tx.CommitAsync(ct);
        return id;
    }

    /// <summary>Responde en el chat de postventa de una venta. Parte el texto en mensajes de hasta 350 caracteres.</summary>
    private async Task<WhatsAppSendResult> SendToMercadoLibrePackAsync(
        ConversacionMercadoLibreConfigDto config,
        string identificadorConversacion,
        string buyerId,
        string text,
        CancellationToken ct)
    {
        if (!config.IsConfiguredForApi || string.IsNullOrWhiteSpace(config.SellerId))
            throw new InvalidOperationException("Falta configurar Mercado Libre para responder mensajes de ventas.");
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new InvalidOperationException("La conversación de Mercado Libre no tiene identificado al comprador.");

        var packId = identificadorConversacion.Trim()[MercadoLibrePackPrefix.Length..];
        var seller = config.SellerId.Trim();
        var url = $"{MercadoLibreApiBase(config)}/messages/packs/{Uri.EscapeDataString(packId)}/sellers/{Uri.EscapeDataString(seller)}?tag=post_sale";
        var ids = new List<string>();
        var ultimoBody = string.Empty;

        foreach (var parte in PartirTextoMercadoLibre(text, MercadoLibreMensajeMaximo))
        {
            var json = JsonSerializer.Serialize(new { from = new { user_id = seller }, to = new { user_id = buyerId.Trim() }, text = parte });
            var (status, body) = await SendMercadoLibreApiAsync(config, () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }, ct);
            if (!IsSuccess(status))
                throw new InvalidOperationException($"Mercado Libre devolvió {(int)status}: {body}");
            ultimoBody = body;
            ids.Add(ReadMercadoLibreSentMessageId(body));
        }

        var messageId = ids.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_MELI",
            WhatsAppMessageId = string.IsNullOrWhiteSpace(messageId)
                ? $"meli-msg-out-{packId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}"
                : $"meli-msg-{messageId}",
            PayloadJson = ultimoBody
        };
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    internal static string ExtractMercadoLibreMessageId(string? resource)
    {
        var value = (resource ?? string.Empty).Trim().TrimEnd('/');
        var corte = value.IndexOf('?');
        if (corte >= 0)
            value = value[..corte];
        var slash = value.LastIndexOf('/');
        return slash >= 0 ? value[(slash + 1)..] : value;
    }

    /// <summary>Parte en trozos de hasta <paramref name="maximo"/> caracteres, cortando en espacios cuando se puede.</summary>
    internal static IReadOnlyList<string> PartirTextoMercadoLibre(string? text, int maximo)
    {
        var resto = (text ?? string.Empty).Trim();
        var partes = new List<string>();
        while (resto.Length > maximo)
        {
            var corte = resto.LastIndexOfAny([' ', '\n'], maximo);
            if (corte < maximo / 2)
                corte = maximo;
            partes.Add(resto[..corte].TrimEnd());
            resto = resto[corte..].TrimStart();
        }

        if (resto.Length > 0)
            partes.Add(resto);
        return partes;
    }

    /// <summary>Lee un mensaje de postventa (formato nuevo con "messages":[...] o el objeto directo).</summary>
    internal static MercadoLibrePostVenta? ParseMercadoLibrePostVenta(string body, string fallbackMessageId)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            JsonElement? elegido = null;
            foreach (var item in messages.EnumerateArray())
            {
                elegido ??= item;
                if (string.Equals(ReadJsonString(item, "id"), fallbackMessageId, StringComparison.OrdinalIgnoreCase))
                {
                    elegido = item;
                    break;
                }
            }

            if (elegido is null)
                return null;
            root = elegido.Value;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var from = root.TryGetProperty("from", out var fromEl) ? FirstNonEmpty(ReadJsonString(fromEl, "user_id"), ReadJsonString(fromEl, "id")) : string.Empty;
        var texto = root.TryGetProperty("text", out var textEl)
            ? textEl.ValueKind == JsonValueKind.Object ? ReadJsonString(textEl, "plain") : textEl.ValueKind == JsonValueKind.String ? textEl.GetString() ?? string.Empty : string.Empty
            : string.Empty;

        var packId = string.Empty;
        if (root.TryGetProperty("message_resources", out var resources) && resources.ValueKind == JsonValueKind.Array)
        {
            foreach (var res in resources.EnumerateArray())
            {
                var name = ReadJsonString(res, "name");
                if (string.Equals(name, "packs", StringComparison.OrdinalIgnoreCase)
                    || (packId.Length == 0 && string.Equals(name, "orders", StringComparison.OrdinalIgnoreCase)))
                    packId = ReadJsonString(res, "id");
            }
        }

        var adjuntos = new List<string>();
        if (root.TryGetProperty("message_attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
            foreach (var att in attachments.EnumerateArray())
                adjuntos.Add(FirstNonEmpty(ReadJsonString(att, "original_filename"), ReadJsonString(att, "filename"), "adjunto"));

        if (string.IsNullOrWhiteSpace(texto))
            texto = adjuntos.Count > 0
                ? $"El comprador envió {(adjuntos.Count == 1 ? "un archivo" : $"{adjuntos.Count} archivos")} por Mercado Libre: {string.Join(", ", adjuntos)}. Abrilo desde Mercado Libre."
                : "Mensaje recibido en Mercado Libre.";

        var fecha = string.Empty;
        if (root.TryGetProperty("message_date", out var dates) && dates.ValueKind == JsonValueKind.Object)
            fecha = FirstNonEmpty(ReadJsonString(dates, "received"), ReadJsonString(dates, "created"));
        fecha = FirstNonEmpty(fecha, ReadJsonString(root, "date_created"), ReadJsonString(root, "date"));

        return new MercadoLibrePostVenta
        {
            MessageId = FirstNonEmpty(ReadJsonString(root, "id"), fallbackMessageId),
            PackId = packId,
            FromUserId = from,
            Texto = texto.Trim(),
            Fecha = string.IsNullOrWhiteSpace(fecha) ? default : ParseMercadoLibreDate(fecha),
            Adjuntos = adjuntos
        };
    }

    private static string ReadMercadoLibreSentMessageId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                root = root[0];
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                root = message;
            return ReadJsonString(root, "id");
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    internal sealed class MercadoLibrePostVenta
    {
        public string MessageId { get; init; } = string.Empty;
        public string PackId { get; init; } = string.Empty;
        public string FromUserId { get; init; } = string.Empty;
        public string Texto { get; init; } = string.Empty;
        public DateTime Fecha { get; init; }
        public IReadOnlyList<string> Adjuntos { get; init; } = [];
    }
}
