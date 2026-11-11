using System.Net.Http.Headers;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Llamadas OAuth a Mercado Libre con la app central de ALFA NET: armar la URL de autorización,
/// canjear el código, renovar el acceso (dura 6 horas; el refresh token es de un solo uso y se
/// reemplaza en cada renovación) y desvincular. No toca bases. Los errores nunca incluyen tokens.
/// </summary>
public sealed class MercadoLibreCentralClient(IHttpClientFactory httpClientFactory, IOptions<MercadoLibreCentralOptions> options)
{
    public const string HttpClientName = "MercadoLibreCentral";

    private readonly MercadoLibreCentralOptions _options = options.Value;

    public string BuildAuthorizeUrl(string redirectUri, string state)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.AuthorizationUrl)
            ? "https://auth.mercadolibre.com.ar/authorization"
            : _options.AuthorizationUrl.Trim();
        return baseUrl
               + "?response_type=code"
               + "&client_id=" + Uri.EscapeDataString(_options.ClientId.Trim())
               + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
               + "&state=" + Uri.EscapeDataString(state);
    }

    public async Task<MercadoLibreCuentaAutorizada> AutorizarAsync(string code, string redirectUri, CancellationToken ct)
    {
        using var tokenDoc = await SendAsync(HttpMethod.Post, $"{_options.ApiBase}/oauth/token", null, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId.Trim(),
            ["client_secret"] = _options.ClientSecret.Trim(),
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        }, "canjear el código de Mercado Libre", ct);
        var tokens = ReadTokens(tokenDoc.RootElement);
        if (string.IsNullOrWhiteSpace(tokens.AccessToken))
            throw new MetaCanalSocialException("Mercado Libre no devolvió el acceso. Volvé a intentar la conexión.");
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
            throw new MetaCanalSocialException("Mercado Libre no devolvió el permiso para mantener la conexión. Revisá que la app tenga activado \"Refresh Token\".");

        using var me = await SendAsync(HttpMethod.Get, $"{_options.ApiBase}/users/me", tokens.AccessToken, null, "leer la cuenta de Mercado Libre", ct);
        var root = me.RootElement;
        var sellerId = MetaCanalesSocialesClient.ReadString(root, "id");
        if (string.IsNullOrWhiteSpace(sellerId))
            sellerId = tokens.UserId;
        if (string.IsNullOrWhiteSpace(sellerId))
            throw new MetaCanalSocialException("Mercado Libre no informó el número de la cuenta.");

        var nombre = string.Join(' ', new[] { MetaCanalesSocialesClient.ReadString(root, "first_name"), MetaCanalesSocialesClient.ReadString(root, "last_name") }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var foto = root.TryGetProperty("thumbnail", out var thumb) ? MetaCanalesSocialesClient.ReadString(thumb, "picture_url") : string.Empty;

        return new MercadoLibreCuentaAutorizada
        {
            SellerId = sellerId,
            Nickname = MetaCanalesSocialesClient.ReadString(root, "nickname"),
            Nombre = nombre,
            SiteId = MetaCanalesSocialesClient.ReadString(root, "site_id"),
            FotoUrl = foto,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            TokenVenceUtc = tokens.VenceUtc
        };
    }

    /// <summary>Renueva el acceso. Devuelve el token nuevo, el refresh token NUEVO (el anterior deja de servir) y el vencimiento.</summary>
    public async Task<MercadoLibreTokens> RenovarAsync(string refreshToken, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"{_options.ApiBase}/oauth/token", null, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId.Trim(),
            ["client_secret"] = _options.ClientSecret.Trim(),
            ["refresh_token"] = refreshToken
        }, "renovar el acceso de Mercado Libre", ct);
        var tokens = ReadTokens(doc.RootElement);
        if (string.IsNullOrWhiteSpace(tokens.AccessToken))
            throw new MetaCanalSocialException("Mercado Libre no devolvió el acceso renovado.");
        return tokens with { RefreshToken = string.IsNullOrWhiteSpace(tokens.RefreshToken) ? refreshToken : tokens.RefreshToken };
    }

    /// <summary>Quita la autorización de la app en la cuenta del vendedor (mejor esfuerzo).</summary>
    public async Task DesvincularAsync(string sellerId, string accessToken, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Delete,
            $"{_options.ApiBase}/users/{Uri.EscapeDataString(sellerId)}/applications/{Uri.EscapeDataString(_options.ClientId.Trim())}",
            accessToken, null, "desvincular la cuenta de Mercado Libre", ct);
    }

    internal static MercadoLibreTokens ReadTokens(JsonElement root)
    {
        var (access, vence) = MetaCanalesSocialesClient.ReadTokenWithExpiry(root);
        return new MercadoLibreTokens(access, MetaCanalesSocialesClient.ReadString(root, "refresh_token"), vence,
            MetaCanalesSocialesClient.ReadString(root, "user_id"));
    }

    /// <summary>Mensaje de error de Mercado Libre ({"message","error"}) sin datos del pedido.</summary>
    internal static string ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var message = MetaCanalesSocialesClient.ReadString(root, "message");
            var error = MetaCanalesSocialesClient.ReadString(root, "error");
            if (string.IsNullOrWhiteSpace(message))
                return error;
            return string.IsNullOrWhiteSpace(error) || message.Contains(error, StringComparison.OrdinalIgnoreCase) ? message : $"{message} ({error})";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, string? bearer, Dictionary<string, string>? form, string accion, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(bearer))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (form is not null)
            request.Content = new FormUrlEncodedContent(form);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new MetaCanalSocialException($"No se pudo comunicar con Mercado Libre para {accion}. Probá de nuevo en unos minutos.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var detalle = ReadError(body);
                throw new MetaCanalSocialException(string.IsNullOrWhiteSpace(detalle)
                    ? $"Mercado Libre rechazó el pedido para {accion} ({(int)response.StatusCode})."
                    : $"Mercado Libre rechazó el pedido para {accion}: {detalle}");
            }

            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            }
            catch (JsonException ex)
            {
                throw new MetaCanalSocialException($"Mercado Libre respondió algo inesperado al {accion}.", ex);
            }
        }
    }
}

public sealed record MercadoLibreTokens(string AccessToken, string RefreshToken, DateTime? VenceUtc, string UserId);
