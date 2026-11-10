using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Webhook central de Instagram y Messenger de la app de ALFA NET. Meta manda a una sola URL los
/// mensajes de todos los clientes; acá se valida la firma, se separa cada "entry" por la cuenta que
/// lo recibió, se busca en ALFA_CENTRAL a qué base pertenece y se procesa en esa base con el mismo
/// código de siempre (RegisterIncomingInstagramWebhookAsync / RegisterIncomingFacebookWebhookAsync).
/// </summary>
public static class MetaCanalesSocialesWebhook
{
    public const string InstagramPath = "/api/conversaciones/meta/instagram/webhook";
    public const string FacebookPath = "/api/conversaciones/meta/facebook/webhook";

    public static IResult Verificar(HttpRequest request, MetaCanalesSocialesOptions options)
    {
        var mode = request.Query["hub.mode"].ToString();
        var verifyToken = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();

        if (string.IsNullOrWhiteSpace(options.WebhookVerifyToken))
            return Results.Problem("Falta MetaCanalesSociales:WebhookVerifyToken en el servidor.", statusCode: StatusCodes.Status500InternalServerError);

        return mode == "subscribe" && string.Equals(verifyToken, options.WebhookVerifyToken.Trim(), StringComparison.Ordinal)
            ? Results.Text(challenge, "text/plain")
            : Results.Unauthorized();
    }

    public static async Task<IResult> RecibirAsync(
        string canal,
        HttpRequest request,
        IServiceScopeFactory scopeFactory,
        MetaCanalesSocialesStore store,
        IOptions<MetaCanalesSocialesOptions> options,
        IOptions<WhatsAppEmbeddedSignupOptions> whatsAppOptions,
        ILogger logger,
        CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawPayload = await reader.ReadToEndAsync(ct);
        var signature = request.Headers["X-Hub-Signature-256"].ToString();

        // Instagram con inicio de sesión de Instagram firma con la clave de la app de Instagram;
        // Messenger con la de la app de Meta. Se acepta cualquiera de las dos configuradas.
        var secretos = new[]
        {
            canal == MetaCanalSocial.Instagram ? options.Value.InstagramAppSecret : string.Empty,
            options.Value.ResolveFacebookAppSecret(whatsAppOptions.Value)
        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        if (secretos.Count == 0)
            return Results.Problem("Falta la clave de la app de Meta en el servidor.", statusCode: StatusCodes.Status500InternalServerError);
        if (!secretos.Any(s => Program.IsValidMetaSignature(rawPayload, s, signature)))
            return Results.Unauthorized();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawPayload);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        var headers = request.Headers.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var procesadas = 0;
        foreach (var grupo in SepararPorCuenta(root))
        {
            int? idBase;
            try
            {
                idBase = await store.ResolverBaseAsync(canal, grupo.CuentaId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook central {Canal}: no se pudo consultar a qué base pertenece la cuenta {Cuenta}.", canal, grupo.CuentaId);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); // Meta reintenta.
            }

            if (idBase is not > 0)
            {
                logger.LogWarning("Webhook central {Canal}: la cuenta {Cuenta} no está conectada a ninguna base; se ignora.", canal, grupo.CuentaId);
                continue;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var bases = scope.ServiceProvider.GetRequiredService<ICentralBasesService>();
            var baseInfo = await bases.GetByIdAsync(idBase.Value, ct);
            if (baseInfo is null)
            {
                logger.LogWarning("Webhook central {Canal}: la base {IdBase} de la cuenta {Cuenta} no existe.", canal, idBase, grupo.CuentaId);
                continue;
            }

            scope.ServiceProvider.GetRequiredService<ISessionService>().SetWebhookOverride(new SessionDto
            {
                BaseId = baseInfo.IdBase,
                Nombre = baseInfo.Nombre,
                Servidor = baseInfo.DbServer,
                BaseDatos = baseInfo.DbName,
                Usuario = baseInfo.DbUser,
                Password = baseInfo.DbPassword,
                TrustServerCertificate = true
            });

            var json = grupo.Payload.ToJsonString();
            using var payload = JsonDocument.Parse(json);
            var svc = scope.ServiceProvider.GetRequiredService<IConversacionesService>();
            var webhookRequest = new ConversacionWebhookRequest { Payload = payload, RawPayload = json, Headers = headers };
            if (canal == MetaCanalSocial.Instagram)
                await svc.RegisterIncomingInstagramWebhookAsync(webhookRequest, ct);
            else
                await svc.RegisterIncomingFacebookWebhookAsync(webhookRequest, ct);
            procesadas++;
        }

        return Results.Ok(new { procesadas });
    }

    /// <summary>
    /// Agrupa los "entry" del aviso por cuenta (entry.id) y arma un aviso por cuenta con el mismo
    /// "object". Un mismo aviso de Meta puede traer mensajes de cuentas de distintos clientes.
    /// </summary>
    internal static IReadOnlyList<(string CuentaId, JsonObject Payload)> SepararPorCuenta(JsonNode? root)
    {
        var result = new List<(string, JsonObject)>();
        if (root is not JsonObject obj || obj["entry"] is not JsonArray entries)
            return result;

        var objeto = obj["object"]?.GetValue<string>() ?? string.Empty;
        foreach (var grupo in entries
                     .OfType<JsonObject>()
                     .GroupBy(e => LeerId(e["id"]), StringComparer.Ordinal)
                     .Where(g => g.Key.Length > 0))
        {
            var array = new JsonArray();
            foreach (var entry in grupo)
                array.Add(entry.DeepClone());
            result.Add((grupo.Key, new JsonObject { ["object"] = objeto, ["entry"] = array }));
        }

        return result;
    }

    private static string LeerId(JsonNode? node)
    {
        if (node is not JsonValue value)
            return string.Empty;
        if (value.TryGetValue<string>(out var s))
            return s.Trim();
        if (value.TryGetValue<long>(out var n))
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return value.ToJsonString().Trim('"');
    }
}
