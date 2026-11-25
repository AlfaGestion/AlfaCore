using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Mcp.Auth;
using AlfaCore.Mcp.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AlfaCore.Mcp.Tests;

/// <summary>Servidor real en memoria (TestServer) con almacen en memoria y configuracion de negocio falsa: no toca ninguna base.</summary>
public sealed class OAuthApp : WebApplicationFactory<Program>
{
    public const string Issuer = "http://localhost";
    public const string Redirect = "https://claude.ai/api/mcp/auth_callback";
    public const string Usuario = "ana";
    public const string Clave = "clave-de-prueba-123";
    private readonly string _clave = Path.Combine(Path.GetTempPath(), "mcp-test-key-" + Guid.NewGuid().ToString("N") + ".pem");

    public OAuthApp()
    {
        void Env(string k, string v) => Environment.SetEnvironmentVariable(k, v);
        Env("ALFACORE_MCP_CONNECTION", "Server=no-se-usa;Database=x");
        Env("Mcp__Auth__Enabled", "true");
        Env("Mcp__Auth__Store", "Memory");
        Env("Mcp__Auth__Issuer", Issuer);
        Env("Mcp__Auth__DevUser", Usuario);
        Env("Mcp__Auth__DevPassword", Clave);
        Env("Mcp__Auth__SigningKeyPath", _clave);
        Env("Mcp__Auth__LimiteLoginPorMin", "10000");
        Env("Mcp__Auth__LimiteTokenPorMin", "10000");
        Env("Mcp__Auth__LimiteRegistroPorMin", "10000");
    }

    public static McpRuntimeSettings Config { get; set; } = Base();
    /// <summary>Simula la base de datos caida al leer la configuracion IA_MCP_*.</summary>
    public static bool BaseCaida { get; set; }
    public static McpRuntimeSettings Base() => new(true, 500, 100, 60, true, true, true, 5m,
        AccessTokenMin: 60, RefreshDias: 30, CodigoSeg: 60, MaxIntentosLogin: 3, BloqueoMin: 15, MaxClientesOauth: 500,
        RedirectsPermitidos: [Redirect, "http://localhost:*/callback"], RequiereTotp: false);

    private sealed class ConfigFalsa : IMcpConfigProvider
    {
        public Task<McpRuntimeSettings> GetAsync(CancellationToken ct) =>
            BaseCaida ? throw new InvalidOperationException("base caida (simulada)") : Task.FromResult(Config);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IMcpConfigProvider>();
            s.AddSingleton<IMcpConfigProvider, ConfigFalsa>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_clave)) File.Delete(_clave);
    }

    public HttpClient Cliente() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
}

public class OAuthFlujoTests : IClassFixture<OAuthApp>
{
    private readonly OAuthApp _app;
    public OAuthFlujoTests(OAuthApp app) { _app = app; OAuthApp.Config = OAuthApp.Base(); }

    // ---- helpers ---------------------------------------------------------------------------------------------------
    private static (string Verifier, string Challenge) Pkce()
    {
        var v = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (v, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(v))));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string> RegistrarCliente(HttpClient c, string redirect = OAuthApp.Redirect)
    {
        var r = await c.PostAsync("/oauth/register", new StringContent(JsonSerializer.Serialize(new { redirect_uris = new[] { redirect }, client_name = "Claude de prueba", token_endpoint_auth_method = "none" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await Json(r)).GetProperty("client_id").GetString()!;
    }

    private static string UrlAutorizar(string clientId, string challenge, string redirect = OAuthApp.Redirect, string? extra = null) =>
        $"/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={challenge}&code_challenge_method=S256&state=xyz&scope=mcp:read&resource={Uri.EscapeDataString(OAuthApp.Issuer + "/mcp")}{extra}";

    private async Task<(HttpResponseMessage Respuesta, string Html)> Login(HttpClient c, string urlAutorizar, string usuario, string clave, string accion = "autorizar", string? codigo = null)
    {
        var get = await c.GetAsync(urlAutorizar);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var html = await get.Content.ReadAsStringAsync();
        var af = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var campos = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\">")
            .Where(m => m.Groups[1].Value != "__RequestVerificationToken")
            .Select(m => KeyValuePair.Create(m.Groups[1].Value, System.Net.WebUtility.HtmlDecode(m.Groups[2].Value))).ToList();
        campos.Add(KeyValuePair.Create("__RequestVerificationToken", af));
        campos.Add(KeyValuePair.Create("usuario", usuario));
        campos.Add(KeyValuePair.Create("clave", clave));
        campos.Add(KeyValuePair.Create("accion", accion));
        if (codigo is not null) campos.Add(KeyValuePair.Create("codigo", codigo));
        var post = await c.PostAsync("/oauth/authorize", new FormUrlEncodedContent(campos));
        return (post, await post.Content.ReadAsStringAsync());
    }

    private async Task<string> ObtenerCodigo(HttpClient c, string clientId, string challenge)
    {
        var (r, _) = await Login(c, UrlAutorizar(clientId, challenge), OAuthApp.Usuario, OAuthApp.Clave);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var q = QueryHelpers.ParseQuery(r.Headers.Location!.Query);
        Assert.StartsWith(OAuthApp.Redirect, r.Headers.Location.ToString());
        Assert.Equal("xyz", q["state"]);                       // state devuelto intacto
        Assert.Equal(OAuthApp.Issuer, q["iss"]);               // RFC 9207
        return q["code"]!;
    }

    private static Task<HttpResponseMessage> Token(HttpClient c, params (string, string)[] campos) =>
        c.PostAsync("/oauth/token", new FormUrlEncodedContent(campos.Select(x => KeyValuePair.Create(x.Item1, x.Item2))));

    private async Task<(string Access, string Refresh, string ClientId)> Sesion(HttpClient c)
    {
        var id = await RegistrarCliente(c);
        var (v, ch) = Pkce();
        var code = await ObtenerCodigo(c, id, ch);
        var r = await Token(c, ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", OAuthApp.Redirect), ("client_id", id), ("code_verifier", v));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        return (j.GetProperty("access_token").GetString()!, j.GetProperty("refresh_token").GetString()!, id);
    }

    private static HttpRequestMessage Mcp(string? bearer, string metodo = "initialize", string? parametros = null)
    {
        var body = metodo == "initialize"
            ? "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}"
            : $"{{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"{metodo}\",\"params\":{parametros ?? "{}"}}}";
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return req;
    }

    // ---- descubrimiento --------------------------------------------------------------------------------------------
    [Fact]
    public async Task Sin_token_el_mcp_responde_401_y_apunta_a_los_metadatos()
    {
        var c = _app.Cliente();
        var r = await c.SendAsync(Mcp(null));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var www = string.Join(" ", r.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("Bearer", www);
        Assert.Contains("resource_metadata=\"" + OAuthApp.Issuer + "/.well-known/oauth-protected-resource", www);
    }

    [Fact]
    public async Task Metadatos_del_recurso_y_del_servidor_de_autorizacion_son_consistentes()
    {
        var c = _app.Cliente();
        var recurso = await Json(await c.GetAsync("/.well-known/oauth-protected-resource/mcp"));
        Assert.Equal(OAuthApp.Issuer + "/mcp", recurso.GetProperty("resource").GetString());
        Assert.Equal(OAuthApp.Issuer, recurso.GetProperty("authorization_servers")[0].GetString());

        foreach (var ruta in new[] { "/.well-known/oauth-authorization-server", "/.well-known/openid-configuration", "/.well-known/oauth-authorization-server/mcp" })
        {
            var m = await Json(await c.GetAsync(ruta));
            Assert.Equal(OAuthApp.Issuer, m.GetProperty("issuer").GetString());
            Assert.Equal(OAuthApp.Issuer + "/oauth/register", m.GetProperty("registration_endpoint").GetString());
            Assert.Equal("S256", m.GetProperty("code_challenge_methods_supported")[0].GetString());
            Assert.Equal("none", m.GetProperty("token_endpoint_auth_methods_supported")[0].GetString());
        }
        var jwks = await Json(await c.GetAsync("/oauth/jwks"));
        Assert.Equal("RSA", jwks.GetProperty("keys")[0].GetProperty("kty").GetString());
        Assert.False(jwks.GetProperty("keys")[0].TryGetProperty("d", out _)); // jamas la parte privada
    }

    // ---- flujo feliz -----------------------------------------------------------------------------------------------
    [Fact]
    public async Task Flujo_completo_registro_login_token_y_llamada_al_mcp()
    {
        var c = _app.Cliente();
        var (access, refresh, _) = await Sesion(c);

        var init = await c.SendAsync(Mcp(access));
        Assert.Equal(HttpStatusCode.OK, init.StatusCode);

        var lista = await c.SendAsync(Mcp(access, "tools/list"));
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        var texto = await lista.Content.ReadAsStringAsync();
        foreach (var herramienta in new[] { "ventas_resumen", "ranking_articulos", "comparar_periodos", "clientes_top", "deuda_clientes", "stock_critico", "consultar_sql" })
            Assert.Contains(herramienta, texto);

        // El token lleva lo esperado.
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(access);
        Assert.Equal(OAuthApp.Issuer, jwt.Issuer);
        Assert.Contains(OAuthApp.Issuer + "/mcp", jwt.Audiences);
        Assert.Equal(OAuthApp.Usuario, jwt.GetClaim("preferred_username").Value);
        Assert.Equal("mcp:read", jwt.GetClaim("scope").Value);
        Assert.False(string.IsNullOrEmpty(refresh));
    }

    [Fact]
    public async Task Login_cancelado_devuelve_access_denied_al_cliente()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();
        var (r, _) = await Login(c, UrlAutorizar(id, ch), "", "", accion: "cancelar");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("access_denied", QueryHelpers.ParseQuery(r.Headers.Location!.Query)["error"]);
    }

    // ---- PKCE, codigos y refresh ------------------------------------------------------------------------------------
    [Fact]
    public async Task Pkce_incorrecto_es_invalid_grant_y_el_codigo_queda_quemado()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (v, ch) = Pkce();
        var code = await ObtenerCodigo(c, id, ch);

        var mal = await Token(c, ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", OAuthApp.Redirect), ("client_id", id), ("code_verifier", Pkce().Verifier));
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        Assert.Equal("invalid_grant", (await Json(mal)).GetProperty("error").GetString());

        var bien = await Token(c, ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", OAuthApp.Redirect), ("client_id", id), ("code_verifier", v));
        Assert.Equal(HttpStatusCode.BadRequest, bien.StatusCode); // un codigo se puede intentar UNA sola vez
    }

    [Fact]
    public async Task Codigo_reutilizado_se_rechaza_y_revoca_los_refresh_ya_emitidos()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (v, ch) = Pkce();
        var code = await ObtenerCodigo(c, id, ch);
        var campos = new[] { ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", OAuthApp.Redirect), ("client_id", id), ("code_verifier", v) };

        var ok = await Token(c, campos);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var refresh = (await Json(ok)).GetProperty("refresh_token").GetString()!;

        var reuso = await Token(c, campos);
        Assert.Equal(HttpStatusCode.BadRequest, reuso.StatusCode);

        var intento = await Token(c, ("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", id));
        Assert.Equal(HttpStatusCode.BadRequest, intento.StatusCode); // alguien reuso el codigo: se cortaron las sesiones
    }

    [Fact]
    public async Task Refresh_rota_y_el_reuso_de_un_token_viejo_revoca_toda_la_cadena()
    {
        var c = _app.Cliente();
        var (_, refresh1, id) = await Sesion(c);

        var r2 = await Token(c, ("grant_type", "refresh_token"), ("refresh_token", refresh1), ("client_id", id));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var j2 = await Json(r2);
        var refresh2 = j2.GetProperty("refresh_token").GetString()!;
        Assert.NotEqual(refresh1, refresh2);

        // El nuevo access token sirve.
        var uso = await c.SendAsync(Mcp(j2.GetProperty("access_token").GetString()));
        Assert.Equal(HttpStatusCode.OK, uso.StatusCode);

        // Reusar el viejo => invalid_grant y se revoca la familia: el nuevo tampoco sirve mas.
        var reuso = await Token(c, ("grant_type", "refresh_token"), ("refresh_token", refresh1), ("client_id", id));
        Assert.Equal(HttpStatusCode.BadRequest, reuso.StatusCode);
        var muerto = await Token(c, ("grant_type", "refresh_token"), ("refresh_token", refresh2), ("client_id", id));
        Assert.Equal(HttpStatusCode.BadRequest, muerto.StatusCode);
    }

    [Fact]
    public async Task Refresh_de_otro_cliente_o_inexistente_se_rechaza()
    {
        var c = _app.Cliente();
        var (_, refresh, _) = await Sesion(c);
        var otro = await RegistrarCliente(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(c, ("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", otro))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(c, ("grant_type", "refresh_token"), ("refresh_token", "inventado"), ("client_id", otro))).StatusCode);
    }

    [Fact]
    public async Task Token_con_cliente_desconocido_o_grant_no_soportado_falla_con_el_error_correcto()
    {
        var c = _app.Cliente();
        var r = await Token(c, ("grant_type", "authorization_code"), ("client_id", "mcp_noexiste"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("invalid_client", (await Json(r)).GetProperty("error").GetString());

        var id = await RegistrarCliente(c);
        var g = await Token(c, ("grant_type", "password"), ("client_id", id));
        Assert.Equal("unsupported_grant_type", (await Json(g)).GetProperty("error").GetString());
        Assert.Equal("no-store", g.Headers.CacheControl?.ToString());
    }

    // ---- registro y redirects --------------------------------------------------------------------------------------
    [Theory]
    [InlineData("https://evil.example.com/callback")]
    [InlineData("http://claude.ai/api/mcp/auth_callback")]             // http fuera de loopback
    [InlineData("https://claude.ai/api/mcp/auth_callback#frag")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://claude.ai/api/mcp/auth_callback/../otro")]
    public async Task Registro_rechaza_redirects_fuera_de_la_lista_blanca(string redirect)
    {
        var r = await _app.Cliente().PostAsync("/oauth/register", new StringContent(JsonSerializer.Serialize(new { redirect_uris = new[] { redirect } }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("invalid_redirect_uri", (await Json(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Registro_acepta_loopback_con_cualquier_puerto_y_rechaza_clientes_confidenciales()
    {
        var c = _app.Cliente();
        await RegistrarCliente(c, "http://localhost:53124/callback");
        var r = await c.PostAsync("/oauth/register", new StringContent(JsonSerializer.Serialize(new { redirect_uris = new[] { OAuthApp.Redirect }, token_endpoint_auth_method = "client_secret_basic" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var basura = await c.PostAsync("/oauth/register", new StringContent("no es json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, basura.StatusCode);
    }

    [Fact]
    public async Task Authorize_con_redirect_no_registrado_o_cliente_falso_muestra_error_y_NO_redirige()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();
        var malRedirect = await c.GetAsync(UrlAutorizar(id, ch, "https://evil.example.com/cb"));
        Assert.Equal(HttpStatusCode.BadRequest, malRedirect.StatusCode);
        Assert.Null(malRedirect.Headers.Location);

        var falso = await c.GetAsync(UrlAutorizar("mcp_falso", ch));
        Assert.Equal(HttpStatusCode.BadRequest, falso.StatusCode);
        Assert.Null(falso.Headers.Location);
    }

    [Fact]
    public async Task Authorize_exige_PKCE_S256_y_resource_propio()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var sinPkce = await c.GetAsync($"/oauth/authorize?response_type=code&client_id={id}&redirect_uri={Uri.EscapeDataString(OAuthApp.Redirect)}&state=s");
        Assert.Equal(HttpStatusCode.Redirect, sinPkce.StatusCode);   // error valido => se informa al cliente ya verificado
        Assert.Equal("invalid_request", QueryHelpers.ParseQuery(sinPkce.Headers.Location!.Query)["error"]);

        var (_, ch) = Pkce();
        var planoLargo = await c.GetAsync(UrlAutorizar(id, ch).Replace("code_challenge_method=S256", "code_challenge_method=plain"));
        Assert.Equal("invalid_request", QueryHelpers.ParseQuery(planoLargo.Headers.Location!.Query)["error"]);

        var otroRecurso = await c.GetAsync(UrlAutorizar(id, ch).Replace(Uri.EscapeDataString(OAuthApp.Issuer + "/mcp"), Uri.EscapeDataString("https://otro.example.com/mcp")));
        Assert.Equal("invalid_target", QueryHelpers.ParseQuery(otroRecurso.Headers.Location!.Query)["error"]);
    }

    // ---- login: clave y bloqueo ------------------------------------------------------------------------------------
    [Fact]
    public async Task Clave_incorrecta_no_entrega_codigo_y_no_distingue_usuario_inexistente()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();
        var (mal, html1) = await Login(c, UrlAutorizar(id, ch), OAuthApp.Usuario, "clave-equivocada-1");
        var (inex, html2) = await Login(c, UrlAutorizar(id, ch), "no-existe", "clave-equivocada-1");
        Assert.Equal(HttpStatusCode.OK, mal.StatusCode);
        Assert.Null(mal.Headers.Location);
        Assert.Contains("incorrectos", html1);
        Assert.Contains("incorrectos", html2);
        Assert.Equal(Regex.Replace(html1, "value=\"[^\"]*\"", ""), Regex.Replace(html2, "value=\"[^\"]*\"", "")); // mismo mensaje: no revela si el usuario existe
    }

    [Fact]
    public async Task Tras_varios_intentos_fallidos_el_usuario_se_bloquea_aun_con_la_clave_correcta()
    {
        var store = _app.Services.GetRequiredService<IAuthStore>();
        await store.CrearUsuarioAsync("bloqueable", "Bloqueable", PasswordHasher.Hash("clave-correcta-123"), null, CancellationToken.None);
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();

        for (var i = 0; i < OAuthApp.Config.MaxIntentosLogin; i++)
            await Login(c, UrlAutorizar(id, ch), "bloqueable", "equivocada-" + i);

        var (r, html) = await Login(c, UrlAutorizar(id, ch), "bloqueable", "clave-correcta-123");
        Assert.Null(r.Headers.Location);
        Assert.Contains("bloqueada", html);
    }

    [Fact]
    public async Task Post_sin_token_antiforgery_se_rechaza()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();
        var r = await c.PostAsync("/oauth/authorize", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = id, ["redirect_uri"] = OAuthApp.Redirect, ["usuario"] = OAuthApp.Usuario, ["clave"] = OAuthApp.Clave, ["accion"] = "autorizar", ["code_challenge"] = ch }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Null(r.Headers.Location);
    }

    // ---- validacion del token en /mcp ------------------------------------------------------------------------------
    [Fact]
    public async Task Tokens_vencidos_de_otro_emisor_o_audiencia_o_sin_scope_se_rechazan()
    {
        var c = _app.Cliente();
        var u = new UsuarioAuth(1, OAuthApp.Usuario, "Ana", "x", true, 0, null, null);

        // Valido, para confirmar que el armado del token de prueba es correcto.
        var ts = _app.Services.GetRequiredService<TokenService>();
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Mcp(ts.CrearAccessToken(u, "c", "mcp:read", TimeSpan.FromMinutes(5), DateTime.UtcNow)))).StatusCode);

        // Vencido.
        var vencido = ts.CrearAccessToken(u, "c", "mcp:read", TimeSpan.FromMinutes(5), DateTime.UtcNow.AddHours(-2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(vencido))).StatusCode);

        // Sin el scope requerido.
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Mcp(ts.CrearAccessToken(u, "c", "otro:scope", TimeSpan.FromMinutes(5), DateTime.UtcNow)))).StatusCode);

        // Firmado con OTRA clave (mismo emisor y audiencia), emisor ajeno y audiencia ajena.
        using var rsa = RSA.Create(2048);
        string Firmar(string iss, string aud) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = iss, Audience = aud, Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object> { ["scope"] = "mcp:read", ["preferred_username"] = "intruso" }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(Firmar(OAuthApp.Issuer, OAuthApp.Issuer + "/mcp")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(Firmar("https://evil.example.com", OAuthApp.Issuer + "/mcp")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp(Firmar(OAuthApp.Issuer, "https://otro.example.com/mcp")))).StatusCode);

        // alg=none.
        var cabecera = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var carga = Base64UrlEncoder.Encode($"{{\"iss\":\"{OAuthApp.Issuer}\",\"aud\":\"{OAuthApp.Issuer}/mcp\",\"scope\":\"mcp:read\",\"exp\":{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Mcp($"{cabecera}.{carga}."))).StatusCode);
    }

    [Fact]
    public async Task Interruptor_IA_MCP_HABILITADO_corta_registro_login_y_token()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        OAuthApp.Config = OAuthApp.Base() with { Habilitado = false };
        try
        {
            var (_, ch) = Pkce();
            Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(UrlAutorizar(id, ch))).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Token(c, ("grant_type", "refresh_token"), ("client_id", id))).StatusCode);
            var reg = await c.PostAsync("/oauth/register", new StringContent("{\"redirect_uris\":[\"" + OAuthApp.Redirect + "\"]}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, reg.StatusCode);
        }
        finally { OAuthApp.Config = OAuthApp.Base(); }
    }

    [Fact]
    public async Task Si_la_base_no_responde_login_registro_y_token_dan_servicio_no_disponible_sin_500_crudo()
    {
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        OAuthApp.BaseCaida = true;
        try
        {
            var (_, ch) = Pkce();
            var login = await c.GetAsync(UrlAutorizar(id, ch));
            Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
            Assert.Contains("no esta disponible", await login.Content.ReadAsStringAsync());
            var reg = await c.PostAsync("/oauth/register", new StringContent("{\"redirect_uris\":[\"" + OAuthApp.Redirect + "\"]}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, reg.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Token(c, ("grant_type", "refresh_token"), ("client_id", id))).StatusCode);
        }
        finally { OAuthApp.BaseCaida = false; }
    }

    // ---- segundo factor (TOTP) -----------------------------------------------------------------------------------------
    private async Task<(string Usuario, string Secreto)> CrearUsuarioConTotp()
    {
        var store = _app.Services.GetRequiredService<IAuthStore>();
        var protector = _app.Services.GetRequiredService<SecretProtector>();
        var usuario = "totp_" + Guid.NewGuid().ToString("N")[..8];
        var id = await store.CrearUsuarioAsync(usuario, usuario, PasswordHasher.Hash("clave-correcta-123"), null, CancellationToken.None);
        var secreto = Totp.NuevoSecreto();
        await store.SetTotpAsync(id, protector.Proteger(secreto), CancellationToken.None);
        return (usuario, secreto);
    }

    private static string CodigoActual(string secreto) => Totp.Codigo(secreto, Totp.PasoDe(DateTime.UtcNow));

    [Fact]
    public async Task Con_segundo_factor_el_login_exige_clave_y_codigo_correctos()
    {
        var (usuario, secreto) = await CrearUsuarioConTotp();
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();

        var sinCodigo = await Login(c, UrlAutorizar(id, ch), usuario, "clave-correcta-123");
        Assert.Null(sinCodigo.Respuesta.Headers.Location);
        var codigoMalo = await Login(c, UrlAutorizar(id, ch), usuario, "clave-correcta-123", codigo: "000000");
        Assert.Null(codigoMalo.Respuesta.Headers.Location);

        var ok = await Login(c, UrlAutorizar(id, ch), usuario, "clave-correcta-123", codigo: CodigoActual(secreto));
        Assert.Equal(HttpStatusCode.Redirect, ok.Respuesta.StatusCode);
        Assert.Contains("code=", ok.Respuesta.Headers.Location!.Query);

        // El codigo correcto pero con la clave equivocada tampoco entra.
        var claveMala = await Login(c, UrlAutorizar(id, ch), usuario, "clave-equivocada-1", codigo: CodigoActual(secreto));
        Assert.Null(claveMala.Respuesta.Headers.Location);
    }

    [Fact]
    public async Task Un_codigo_TOTP_ya_usado_no_sirve_de_nuevo()
    {
        var (usuario, secreto) = await CrearUsuarioConTotp();
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();
        var codigo = CodigoActual(secreto);

        var primero = await Login(c, UrlAutorizar(id, ch), usuario, "clave-correcta-123", codigo: codigo);
        Assert.Equal(HttpStatusCode.Redirect, primero.Respuesta.StatusCode);
        var repetido = await Login(c, UrlAutorizar(id, ch), usuario, "clave-correcta-123", codigo: codigo);   // alguien que espio el codigo
        Assert.Null(repetido.Respuesta.Headers.Location);
    }

    [Fact]
    public async Task Los_codigos_TOTP_erroneos_cuentan_para_el_bloqueo_aun_con_la_clave_correcta()
    {
        var (usuario, secreto) = await CrearUsuarioConTotp();
        var c = _app.Cliente();
        var id = await RegistrarCliente(c);
        var (_, ch) = Pkce();

        for (var i = 0; i < OAuthApp.Config.MaxIntentosLogin; i++)
            await Login(c, UrlAutorizar(id, ch), usuario, "clave-correcta-123", codigo: "11111" + i);   // adivinar el codigo de 6 digitos

        var despues = await Login(c, UrlAutorizar(id, ch), usuario, "clave-correcta-123", codigo: CodigoActual(secreto));
        Assert.Null(despues.Respuesta.Headers.Location);   // bloqueado: ni el codigo correcto entra
        Assert.Contains("bloqueada", despues.Html);
    }

    [Fact]
    public async Task Si_se_exige_segundo_factor_un_usuario_sin_TOTP_no_puede_ingresar_ni_con_la_clave_correcta()
    {
        OAuthApp.Config = OAuthApp.Base() with { RequiereTotp = true };
        try
        {
            var c = _app.Cliente();
            var id = await RegistrarCliente(c);
            var (_, ch) = Pkce();
            var (r, html) = await Login(c, UrlAutorizar(id, ch), OAuthApp.Usuario, OAuthApp.Clave);
            Assert.Null(r.Headers.Location);
            Assert.Contains("segundo factor", html);   // mensaje generico que orienta sin revelar que la clave era correcta
            Assert.Contains("required", html);          // el campo del codigo es obligatorio
        }
        finally { OAuthApp.Config = OAuthApp.Base(); }
    }

    [Fact]
    public async Task El_secreto_TOTP_se_guarda_cifrado_y_nunca_en_claro()
    {
        var (usuario, secreto) = await CrearUsuarioConTotp();
        var u = await _app.Services.GetRequiredService<IAuthStore>().GetUsuarioAsync(usuario, CancellationToken.None);
        Assert.NotNull(u!.TotpSecreto);
        Assert.StartsWith("v1.", u.TotpSecreto);
        Assert.DoesNotContain(secreto, u.TotpSecreto);
        Assert.Equal(secreto, _app.Services.GetRequiredService<SecretProtector>().Revelar(u.TotpSecreto));
    }

    // ---- cabeceras de seguridad ------------------------------------------------------------------------------------
    [Fact]
    public async Task La_pantalla_de_login_no_se_puede_embeber_ni_cachear_y_escapa_el_nombre_del_cliente()
    {
        var c = _app.Cliente();
        var r = await c.PostAsync("/oauth/register", new StringContent(JsonSerializer.Serialize(new { redirect_uris = new[] { OAuthApp.Redirect }, client_name = "<script>alert(1)</script>" }), Encoding.UTF8, "application/json"));
        var id = (await Json(r)).GetProperty("client_id").GetString()!;
        var (_, ch) = Pkce();
        var get = await c.GetAsync(UrlAutorizar(id, ch));
        var html = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Equal("DENY", get.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", get.Headers.GetValues("Content-Security-Policy").Single());
    }
}

public class PrimitivasOAuthTests
{
    [Theory]   // vectores oficiales de RFC 6238 (secreto ASCII "12345678901234567890", SHA-1), recortados a 6 digitos
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Totp_cumple_los_vectores_del_RFC_6238(long segundos, string esperado)
    {
        var secreto = Base32.Codificar(System.Text.Encoding.ASCII.GetBytes("12345678901234567890"));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", secreto);
        Assert.Equal(esperado, Totp.Codigo(secreto, segundos / Totp.PeriodoSeg));
    }

    [Fact]
    public void Base32_ida_y_vuelta_y_rechazo_de_basura()
    {
        var datos = RandomNumberGenerator.GetBytes(20);
        Assert.Equal(datos, Base32.Decodificar(Base32.Codificar(datos)));
        Assert.Equal(datos, Base32.Decodificar(Base32.Codificar(datos).ToLowerInvariant()));
        Assert.Throws<FormatException>(() => Base32.Decodificar("no es base32 !!"));
    }

    [Fact]
    public void Totp_acepta_el_paso_actual_y_uno_de_margen_pero_no_dos_pasos_ni_basura()
    {
        var secreto = Totp.NuevoSecreto();
        var ahora = new DateTime(2026, 11, 25, 12, 0, 10, DateTimeKind.Utc);
        var paso = Totp.PasoDe(ahora);
        Assert.Equal((true, paso), Totp.Verificar(secreto, Totp.Codigo(secreto, paso), ahora));
        Assert.Equal((true, paso - 1), Totp.Verificar(secreto, Totp.Codigo(secreto, paso - 1), ahora));
        Assert.Equal((true, paso + 1), Totp.Verificar(secreto, Totp.Codigo(secreto, paso + 1), ahora));
        Assert.False(Totp.Verificar(secreto, Totp.Codigo(secreto, paso - 2), ahora).Ok);
        Assert.False(Totp.Verificar(secreto, Totp.Codigo(secreto, paso + 2), ahora).Ok);
        Assert.False(Totp.Verificar(secreto, "12345", ahora).Ok);
        Assert.False(Totp.Verificar(secreto, "abcdef", ahora).Ok);
        Assert.False(Totp.Verificar(secreto, null, ahora).Ok);
        Assert.True(Totp.Verificar(secreto, Totp.Codigo(secreto, paso).Insert(3, " "), ahora).Ok);   // la app a veces muestra "123 456"
    }

    [Fact]
    public void El_QR_de_inscripcion_es_un_PNG_valido()
    {
        var uri = Totp.UriOtpAuth(Totp.NuevoSecreto(), "ana", "AlfaCore MCP");
        var png = new QRCoder.PngByteQRCode(new QRCoder.QRCodeGenerator().CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.M)).GetGraphic(10);
        Assert.True(png.Length > 500);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);   // firma PNG
    }

    [Fact]
    public void La_URI_otpauth_lleva_los_parametros_que_esperan_las_apps()
    {
        var uri = Totp.UriOtpAuth("ABCDEFGH", "ana@x", "AlfaCore MCP");
        Assert.StartsWith("otpauth://totp/AlfaCore%20MCP:ana%40x?secret=ABCDEFGH", uri);
        Assert.Contains("algorithm=SHA1&digits=6&period=30", uri);
    }

    [Fact]
    public void Hash_de_clave_es_verificable_distinto_por_sal_y_rechaza_basura()
    {
        var h1 = PasswordHasher.Hash("una-clave-larga-123");
        var h2 = PasswordHasher.Hash("una-clave-larga-123");
        Assert.NotEqual(h1, h2);
        Assert.True(PasswordHasher.Verificar("una-clave-larga-123", h1));
        Assert.False(PasswordHasher.Verificar("otra", h1));
        Assert.False(PasswordHasher.Verificar("x", "no-es-un-hash"));
        Assert.False(PasswordHasher.Verificar("x", "pbkdf2-sha256$1$AAAA$AAAA")); // pocas iteraciones => se rechaza
        Assert.DoesNotContain("una-clave-larga-123", h1);
    }

    [Fact]
    public void Pkce_valida_solo_S256_con_verifier_correcto()
    {
        var v = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var ch = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(v)));
        Assert.True(Pkce.ChallengeValido(ch));
        Assert.True(Pkce.VerificarS256(v, ch));
        Assert.False(Pkce.VerificarS256(v + "x", ch));
        Assert.False(Pkce.VerificarS256("corto", ch));
        Assert.False(Pkce.VerificarS256(null, ch));
        Assert.False(Pkce.ChallengeValido("demasiado-corto"));
        // (plain se rechaza por code_challenge_method != S256 en /authorize; ver Authorize_exige_PKCE_S256_y_resource_propio)
    }

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("http://localhost:8123/callback", true)]
    [InlineData("http://127.0.0.1:1/callback", true)]
    [InlineData("http://localhost:8123/otra", false)]
    [InlineData("http://localhost:abc/callback", false)]
    [InlineData("https://localhost.evil.com:8123/callback", false)]
    [InlineData("https://claude.ai/api/mcp/auth_callback?x=1", false)]
    [InlineData("http://claude.ai/api/mcp/auth_callback", false)]
    [InlineData("", false)]
    public void Politica_de_redirects(string uri, bool esperado) =>
        Assert.Equal(esperado, RedirectPolicy.Permitida(uri, ["https://claude.ai/api/mcp/auth_callback", "http://localhost:*/callback", "http://127.0.0.1:*/callback"]));

    [Fact]
    public void Redirect_pedido_debe_ser_el_registrado_y_en_loopback_se_ignora_el_puerto()
    {
        Assert.True(RedirectPolicy.CoincideConRegistrada("https://claude.ai/api/mcp/auth_callback", ["https://claude.ai/api/mcp/auth_callback"]));
        Assert.False(RedirectPolicy.CoincideConRegistrada("https://claude.ai/api/mcp/auth_callback/x", ["https://claude.ai/api/mcp/auth_callback"]));
        Assert.True(RedirectPolicy.CoincideConRegistrada("http://localhost:60000/callback", ["http://localhost:53124/callback"]));
        Assert.False(RedirectPolicy.CoincideConRegistrada("http://localhost:60000/robo", ["http://localhost:53124/callback"]));
        Assert.False(RedirectPolicy.CoincideConRegistrada("https://evil.com/callback", ["http://localhost:53124/callback"]));
    }

    [Fact]
    public void Los_secretos_son_aleatorios_y_se_guardan_solo_como_hash()
    {
        var a = Secretos.NuevoToken();
        Assert.NotEqual(a, Secretos.NuevoToken());
        Assert.True(a.Length >= 43);
        Assert.Equal(64, Secretos.Hash(a).Length);
        Assert.DoesNotContain(a, Secretos.Hash(a));
    }
}
