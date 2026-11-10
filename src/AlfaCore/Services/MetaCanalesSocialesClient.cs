using System.Globalization;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Llamadas a Meta para la conexión guiada de Instagram (Instagram API con inicio de sesión de Instagram)
/// y Facebook Messenger (inicio de sesión de Facebook + páginas). Solo habla con Meta: no toca bases.
/// Usa un HttpClient sin logs porque varios pedidos llevan tokens en la URL.
/// Los mensajes de error nunca incluyen tokens.
/// </summary>
public sealed class MetaCanalesSocialesClient(
    IHttpClientFactory httpClientFactory,
    IOptions<MetaCanalesSocialesOptions> options,
    IOptions<WhatsAppEmbeddedSignupOptions> whatsAppOptions)
{
    public const string HttpClientName = "MetaCanalesSociales";

    private readonly MetaCanalesSocialesOptions _options = options.Value;
    private readonly WhatsAppEmbeddedSignupOptions _whatsApp = whatsAppOptions.Value;

    private string Version => string.IsNullOrWhiteSpace(_options.GraphApiVersion) ? "v23.0" : _options.GraphApiVersion.Trim();

    // ---------- Instagram ----------

    public string BuildInstagramAuthorizeUrl(string redirectUri, string state)
        => "https://www.instagram.com/oauth/authorize"
           + "?enable_fb_login=0&force_reauth=true"
           + "&client_id=" + Uri.EscapeDataString(_options.InstagramAppId.Trim())
           + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
           + "&response_type=code"
           + "&scope=" + Uri.EscapeDataString(NormalizeScopes(_options.InstagramScopes))
           + "&state=" + Uri.EscapeDataString(state);

    public async Task<MetaInstagramCuentaAutorizada> InstagramAutorizarAsync(string code, string redirectUri, CancellationToken ct)
    {
        // 1) Código → token corto.
        using var shortDoc = await SendAsync(HttpMethod.Post, "https://api.instagram.com/oauth/access_token",
            new Dictionary<string, string>
            {
                ["client_id"] = _options.InstagramAppId.Trim(),
                ["client_secret"] = _options.InstagramAppSecret.Trim(),
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["code"] = code
            }, "canjear el código de Instagram", ct);
        var shortRoot = shortDoc.RootElement;
        if (shortRoot.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            shortRoot = data[0];
        var shortToken = ReadString(shortRoot, "access_token");
        if (string.IsNullOrWhiteSpace(shortToken))
            throw new MetaCanalSocialException("Instagram no devolvió el acceso. Volvé a intentar la conexión.");

        // 2) Token corto → token largo (60 días).
        var (token, venceUtc) = await InstagramTokenLargoAsync(shortToken, ct);

        // 3) Datos de la cuenta profesional.
        using var me = await SendAsync(HttpMethod.Get,
            $"https://graph.instagram.com/{Version}/me?fields=user_id,id,username,name,profile_picture_url,account_type&access_token={Uri.EscapeDataString(token)}",
            null, "leer la cuenta de Instagram", ct);
        var root = me.RootElement;
        var userId = ReadString(root, "user_id");
        var appScopedId = ReadString(root, "id");
        if (string.IsNullOrWhiteSpace(userId))
            userId = appScopedId;
        if (string.IsNullOrWhiteSpace(userId))
            throw new MetaCanalSocialException("Instagram no informó el número de la cuenta.");

        return new MetaInstagramCuentaAutorizada
        {
            CuentaId = userId,
            CuentaIdAlterno = string.Equals(appScopedId, userId, StringComparison.Ordinal) ? string.Empty : appScopedId,
            Usuario = ReadString(root, "username"),
            Nombre = ReadString(root, "name"),
            FotoUrl = ReadString(root, "profile_picture_url"),
            Token = token,
            TokenVenceUtc = venceUtc
        };
    }

    private async Task<(string Token, DateTime? VenceUtc)> InstagramTokenLargoAsync(string shortToken, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get,
            "https://graph.instagram.com/access_token?grant_type=ig_exchange_token"
            + "&client_secret=" + Uri.EscapeDataString(_options.InstagramAppSecret.Trim())
            + "&access_token=" + Uri.EscapeDataString(shortToken),
            null, "obtener el acceso permanente de Instagram", ct);
        return ReadTokenWithExpiry(doc.RootElement);
    }

    /// <summary>Renueva el token largo (sirve si tiene más de 24 h y no venció). Devuelve el nuevo y su vencimiento.</summary>
    public async Task<(string Token, DateTime? VenceUtc)> InstagramRenovarAsync(string token, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get,
            "https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token=" + Uri.EscapeDataString(token),
            null, "renovar el acceso de Instagram", ct);
        var result = ReadTokenWithExpiry(doc.RootElement);
        if (string.IsNullOrWhiteSpace(result.Token))
            throw new MetaCanalSocialException("Instagram no devolvió el acceso renovado.");
        return result;
    }

    public async Task InstagramSuscribirAsync(string token, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Post,
            $"https://graph.instagram.com/{Version}/me/subscribed_apps?subscribed_fields=messages,messaging_postbacks&access_token={Uri.EscapeDataString(token)}",
            null, "activar los avisos de mensajes de Instagram", ct);
        EnsureSuccessFlag(doc.RootElement, "Instagram no confirmó la activación de los avisos de mensajes.");
    }

    public async Task InstagramDesuscribirAsync(string token, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Delete,
            $"https://graph.instagram.com/{Version}/me/subscribed_apps?access_token={Uri.EscapeDataString(token)}",
            null, "desactivar los avisos de Instagram", ct);
    }

    // ---------- Facebook ----------

    public string BuildFacebookAuthorizeUrl(string redirectUri, string state)
        => $"https://www.facebook.com/{Version}/dialog/oauth"
           + "?client_id=" + Uri.EscapeDataString(_options.ResolveFacebookAppId(_whatsApp))
           + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
           + "&response_type=code"
           + "&auth_type=rerequest"
           + "&scope=" + Uri.EscapeDataString(NormalizeScopes(_options.FacebookScopes))
           + "&state=" + Uri.EscapeDataString(state);

    /// <summary>Canjea el código y devuelve las páginas que la persona administra, cada una con su token (que no vence).</summary>
    public async Task<IReadOnlyList<MetaFacebookPaginaAutorizada>> FacebookAutorizarAsync(string code, string redirectUri, CancellationToken ct)
    {
        var appId = _options.ResolveFacebookAppId(_whatsApp);
        var appSecret = _options.ResolveFacebookAppSecret(_whatsApp);

        using var shortDoc = await SendAsync(HttpMethod.Get,
            $"https://graph.facebook.com/{Version}/oauth/access_token"
            + "?client_id=" + Uri.EscapeDataString(appId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&client_secret=" + Uri.EscapeDataString(appSecret)
            + "&code=" + Uri.EscapeDataString(code),
            null, "canjear el código de Facebook", ct);
        var shortToken = ReadString(shortDoc.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(shortToken))
            throw new MetaCanalSocialException("Facebook no devolvió el acceso. Volvé a intentar la conexión.");

        // Con el token largo del usuario, los tokens de página no vencen.
        using var longDoc = await SendAsync(HttpMethod.Get,
            $"https://graph.facebook.com/{Version}/oauth/access_token?grant_type=fb_exchange_token"
            + "&client_id=" + Uri.EscapeDataString(appId)
            + "&client_secret=" + Uri.EscapeDataString(appSecret)
            + "&fb_exchange_token=" + Uri.EscapeDataString(shortToken),
            null, "obtener el acceso permanente de Facebook", ct);
        var userToken = ReadString(longDoc.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(userToken))
            userToken = shortToken;

        var pages = new List<MetaFacebookPaginaAutorizada>();
        string? url = $"https://graph.facebook.com/{Version}/me/accounts?fields=id,name,username,access_token,tasks,picture%7Burl%7D&limit=100&access_token={Uri.EscapeDataString(userToken)}";
        for (var vuelta = 0; url is not null && vuelta < 10; vuelta++)
        {
            using var doc = await SendAsync(HttpMethod.Get, url, null, "leer las páginas de Facebook", ct);
            pages.AddRange(ParseFacebookPages(doc.RootElement));
            url = doc.RootElement.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next)
                ? next.GetString()
                : null;
        }

        return pages;
    }

    public async Task FacebookSuscribirPaginaAsync(string pageId, string pageToken, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Post,
            $"https://graph.facebook.com/{Version}/{Uri.EscapeDataString(pageId)}/subscribed_apps?subscribed_fields=messages,messaging_postbacks&access_token={Uri.EscapeDataString(pageToken)}",
            null, "activar los avisos de mensajes de la página", ct);
        EnsureSuccessFlag(doc.RootElement, "Facebook no confirmó la activación de los avisos de mensajes de la página.");
    }

    public async Task FacebookDesuscribirPaginaAsync(string pageId, string pageToken, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Delete,
            $"https://graph.facebook.com/{Version}/{Uri.EscapeDataString(pageId)}/subscribed_apps?access_token={Uri.EscapeDataString(pageToken)}",
            null, "desactivar los avisos de la página", ct);
    }

    // ---------- Parsing (internal para tests) ----------

    internal static IReadOnlyList<MetaFacebookPaginaAutorizada> ParseFacebookPages(JsonElement root)
    {
        var result = new List<MetaFacebookPaginaAutorizada>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var token = ReadString(item, "access_token");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(token))
                continue;

            var puedeMensajes = true;
            if (item.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
            {
                var lista = tasks.EnumerateArray().Select(t => t.GetString() ?? string.Empty).ToList();
                puedeMensajes = lista.Any(t => t is "MESSAGING" or "MODERATE" or "MANAGE");
            }

            var foto = item.TryGetProperty("picture", out var picture)
                       && picture.TryGetProperty("data", out var pictureData)
                ? ReadString(pictureData, "url")
                : string.Empty;

            result.Add(new MetaFacebookPaginaAutorizada
            {
                PaginaId = id,
                Nombre = ReadString(item, "name"),
                Usuario = ReadString(item, "username"),
                FotoUrl = foto,
                Token = token,
                PuedeMensajes = puedeMensajes
            });
        }

        return result;
    }

    internal static (string Token, DateTime? VenceUtc) ReadTokenWithExpiry(JsonElement root)
    {
        var token = ReadString(root, "access_token");
        DateTime? vence = null;
        if (root.TryGetProperty("expires_in", out var expires))
        {
            long seconds = expires.ValueKind switch
            {
                JsonValueKind.Number when expires.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(expires.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
                _ => 0
            };
            if (seconds > 0)
                vence = DateTime.UtcNow.AddSeconds(seconds);
        }

        return (token, vence);
    }

    /// <summary>Lee el mensaje de error de Meta (Graph o Instagram) sin exponer nada del pedido.</summary>
    internal static string ReadMetaError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    var message = ReadString(error, "error_user_msg");
                    if (string.IsNullOrWhiteSpace(message))
                        message = ReadString(error, "message");
                    var code = ReadString(error, "code");
                    return string.IsNullOrWhiteSpace(code) ? message : $"{message} (código {code})";
                }

                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? string.Empty;
            }

            var igMessage = ReadString(root, "error_message");
            if (!string.IsNullOrWhiteSpace(igMessage))
                return igMessage;
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    internal static string ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    internal static string NormalizeScopes(string? scopes)
        => string.Join(',', (scopes ?? string.Empty)
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static void EnsureSuccessFlag(JsonElement root, string message)
    {
        if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            throw new MetaCanalSocialException(message);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, Dictionary<string, string>? form, string accion, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (form is not null)
            request.Content = new FormUrlEncodedContent(form);

        var client = httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new MetaCanalSocialException($"No se pudo comunicar con Meta para {accion}. Probá de nuevo en unos minutos.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var detalle = ReadMetaError(body);
                throw new MetaCanalSocialException(string.IsNullOrWhiteSpace(detalle)
                    ? $"Meta rechazó el pedido para {accion} ({(int)response.StatusCode})."
                    : $"Meta rechazó el pedido para {accion}: {detalle}");
            }

            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            }
            catch (JsonException ex)
            {
                throw new MetaCanalSocialException($"Meta respondió algo inesperado al {accion}.", ex);
            }
        }
    }
}
