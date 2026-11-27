using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Mcp.Auth;
using AlfaCore.Mcp.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AlfaCore.Mcp.Tests;

/// <summary>Servidor en memoria con accesos multi-base en memoria y un "SMTP" que guarda los mails para leer el codigo.</summary>
public sealed class OAuthEmailApp : WebApplicationFactory<Program>
{
    public const string Issuer = "http://localhost";
    public const string Redirect = "https://claude.ai/api/mcp/auth_callback";
    private readonly string _clave = Path.Combine(Path.GetTempPath(), "mcp-test-key-" + Guid.NewGuid().ToString("N") + ".pem");

    public OAuthEmailApp()
    {
        void Env(string k, string v) => Environment.SetEnvironmentVariable(k, v);
        Env("ALFACORE_MCP_CONNECTION", "Server=no-se-usa;Database=x");
        Env("Mcp__Auth__Enabled", "true");
        Env("Mcp__Auth__Store", "Memory");
        Env("Mcp__Auth__Issuer", Issuer);
        Env("Mcp__Auth__SigningKeyPath", _clave);
        Env("Mcp__Auth__LimiteLoginPorMin", "10000");
        Env("Mcp__Auth__LimiteTokenPorMin", "10000");
        Env("Mcp__Auth__LimiteRegistroPorMin", "10000");
    }

    public static readonly McpRuntimeSettings Config = new(true, 500, 100, 60, true, true, true, 5m,
        AccessTokenMin: 60, RefreshDias: 30, CodigoSeg: 60, MaxIntentosLogin: 3, BloqueoMin: 15, MaxClientesOauth: 500,
        RedirectsPermitidos: [Redirect], RequiereTotp: true, EmailHabilitado: true, EmailCodigoMin: 10, EmailMaxEnvios: 3, EmailAvisoConexion: true);

    public sealed class Buzon : IEmailSender
    {
        public ConcurrentQueue<MensajeEmail> Enviados { get; } = new();
        public bool Disponible => true;
        public Task EnviarAsync(MensajeEmail m, CancellationToken ct) { Enviados.Enqueue(m); return Task.CompletedTask; }
        public List<MensajeEmail> Para(string email) => Enviados.Where(m => m.Para == email).ToList();
        public string UltimoCodigo(string email) =>
            Regex.Match(Para(email).Last(m => m.Asunto.Contains("código")).Texto, @"\b(\d{6})\b").Groups[1].Value;
    }

    private sealed class ConfigFija : IMcpConfigProvider
    {
        public Task<McpRuntimeSettings> GetAsync(CancellationToken ct) => Task.FromResult(Config);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IMcpConfigProvider>();
            s.AddSingleton<IMcpConfigProvider, ConfigFija>();
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<Buzon>();
            s.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<Buzon>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_clave)) File.Delete(_clave);
    }

    public Buzon Mails => Services.GetRequiredService<Buzon>();
    public MemoryAccesoStore Accesos => Services.GetRequiredService<MemoryAccesoStore>();
    public HttpClient Cliente() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
}

[Collection("OAuth-env")]
public class OAuthEmailTests : IClassFixture<OAuthEmailApp>
{
    private readonly OAuthEmailApp _app;
    public OAuthEmailTests(OAuthEmailApp app) => _app = app;

    private static string MailUnico() => $"u{Guid.NewGuid():N}"[..12] + "@cliente.com";
    private static int BaseUnica() => RandomNumberGenerator.GetInt32(1000, 1_000_000);

    // ---- helpers ---------------------------------------------------------------------------------------------------
    private static (string Verifier, string Challenge) Pkce()
    {
        var v = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (v, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(v))));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(string ClientId, string Verifier, string Html)> Abrir(HttpClient c)
    {
        var r = await c.PostAsync("/oauth/register", new StringContent(JsonSerializer.Serialize(new { redirect_uris = new[] { OAuthEmailApp.Redirect }, client_name = "Claude" }), Encoding.UTF8, "application/json"));
        var id = (await Json(r)).GetProperty("client_id").GetString()!;
        var (v, ch) = Pkce();
        var get = await c.GetAsync($"/oauth/authorize?response_type=code&client_id={id}&redirect_uri={Uri.EscapeDataString(OAuthEmailApp.Redirect)}&code_challenge={ch}&code_challenge_method=S256&state=xyz&scope=mcp:read");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        return (id, v, await get.Content.ReadAsStringAsync());
    }

    /// <summary>Envia el formulario de la pagina actual (campos ocultos + antiforgery) con los campos extra.</summary>
    private static async Task<(HttpResponseMessage R, string Html)> Enviar(HttpClient c, string html, params (string K, string V)[] extra)
    {
        var campos = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\">")
            .Select(m => KeyValuePair.Create(m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Where(kv => extra.All(e => e.K != kv.Key)).ToList();
        campos.AddRange(extra.Select(e => KeyValuePair.Create(e.K, e.V)));
        var r = await c.PostAsync("/oauth/authorize", new FormUrlEncodedContent(campos));
        return (r, await r.Content.ReadAsStringAsync());
    }

    private async Task<(HttpResponseMessage R, string Html)> PedirYUsarCodigo(HttpClient c, string html, string email)
    {
        var (_, paginaCodigo) = await Enviar(c, html, ("email", email), ("accion", "enviar-codigo"));
        return await Enviar(c, paginaCodigo, ("codigo_email", _app.Mails.UltimoCodigo(email)), ("accion", "verificar-codigo"));
    }

    private static async Task<JsonElement> Canjear(HttpClient c, HttpResponseMessage redirect, string clientId, string verifier)
    {
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var code = QueryHelpers.ParseQuery(redirect.Headers.Location!.Query)["code"].ToString();
        var r = await c.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = OAuthEmailApp.Redirect, ["client_id"] = clientId, ["code_verifier"] = verifier
        }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static Dictionary<string, string> Claims(string jwt) =>
        new JsonWebToken(jwt).Claims.GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First().Value);

    // ---- tests -----------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Por_defecto_la_pantalla_pide_el_mail_y_ofrece_usuario_y_clave_aparte()
    {
        var (_, _, html) = await Abrir(_app.Cliente());
        Assert.Contains("name=\"email\"", html);
        Assert.DoesNotContain("name=\"clave\"", html);
        Assert.Contains("modo=clave", html);
    }

    [Fact]
    public async Task Flujo_por_mail_entrega_un_token_atado_a_la_base_y_avisa_la_conexion()
    {
        var email = MailUnico(); var idBase = BaseUnica();
        _app.Accesos.Sembrar(idBase, "Panaderia Prueba", "C1", email);
        var c = _app.Cliente();
        var (id, v, html) = await Abrir(c);

        var (r, _) = await PedirYUsarCodigo(c, html, email);
        var tokens = await Canjear(c, r, id, v);
        var claims = Claims(tokens.GetProperty("access_token").GetString()!);
        Assert.Equal(idBase.ToString(), claims["base"]);
        Assert.Equal("email", claims["amr"]);
        Assert.Equal(email, claims["preferred_username"]);
        Assert.Contains(_app.Mails.Para(email), m => m.Asunto.Contains("Nueva conexión") && m.Texto.Contains("Panaderia Prueba"));

        // La renovacion conserva la base.
        var rr = await c.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.GetProperty("refresh_token").GetString()!, ["client_id"] = id }));
        Assert.Equal(HttpStatusCode.OK, rr.StatusCode);
        Assert.Equal(idBase.ToString(), Claims((await Json(rr)).GetProperty("access_token").GetString()!)["base"]);
    }

    [Fact]
    public async Task Un_mail_sin_acceso_ve_la_misma_respuesta_y_no_se_le_envia_nada()
    {
        var c = _app.Cliente();
        var (_, _, html) = await Abrir(c);
        var email = MailUnico();
        var (r, pagina) = await Enviar(c, html, ("email", email), ("accion", "enviar-codigo"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Si " + email + " tiene acceso habilitado", pagina);
        Assert.Empty(_app.Mails.Para(email));
    }

    [Fact]
    public async Task Un_mail_del_cliente_sin_acceso_MCP_o_con_la_base_no_habilitada_no_recibe_codigo()
    {
        var sinAcceso = MailUnico(); var noHabilitada = MailUnico();
        _app.Accesos.Sembrar(BaseUnica(), "B", "C2", sinAcceso, acceso: false);
        _app.Accesos.Sembrar(BaseUnica(), "B", "C3", noHabilitada, habilitada: false);
        var c = _app.Cliente();
        var (_, _, html) = await Abrir(c);
        await Enviar(c, html, ("email", sinAcceso), ("accion", "enviar-codigo"));
        await Enviar(c, html, ("email", noHabilitada), ("accion", "enviar-codigo"));
        Assert.Empty(_app.Mails.Para(sinAcceso));
        Assert.Empty(_app.Mails.Para(noHabilitada));
    }

    [Fact]
    public async Task Codigo_incorrecto_no_entra_y_tras_los_intentos_permitidos_ni_el_correcto_sirve()
    {
        var email = MailUnico();
        _app.Accesos.Sembrar(BaseUnica(), "B", "C4", email);
        var c = _app.Cliente();
        var (_, _, html) = await Abrir(c);
        var (_, pagina) = await Enviar(c, html, ("email", email), ("accion", "enviar-codigo"));
        var bueno = _app.Mails.UltimoCodigo(email);
        var malo = bueno == "000000" ? "111111" : "000000";
        for (var i = 0; i < OAuthEmailApp.Config.MaxIntentosLogin; i++)
        {
            var (r, p) = await Enviar(c, pagina, ("codigo_email", malo), ("accion", "verificar-codigo"));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("Codigo incorrecto", p);
        }
        var (fin, _) = await Enviar(c, pagina, ("codigo_email", bueno), ("accion", "verificar-codigo"));
        Assert.NotEqual(HttpStatusCode.Redirect, fin.StatusCode);
    }

    [Fact]
    public async Task El_codigo_es_de_un_solo_uso_y_no_sirve_para_otro_mail()
    {
        var a = MailUnico(); var b = MailUnico(); var idBase = BaseUnica();
        _app.Accesos.Sembrar(idBase, "B", "C5", a);
        _app.Accesos.Sembrar(idBase, "B", "C5", b);
        var c = _app.Cliente();
        var (_, _, html) = await Abrir(c);
        var (_, pagina) = await Enviar(c, html, ("email", a), ("accion", "enviar-codigo"));
        var codigoA = _app.Mails.UltimoCodigo(a);

        await Enviar(c, html, ("email", b), ("accion", "enviar-codigo"));
        var (otro, _) = await Enviar(c, pagina, ("email", b), ("codigo_email", codigoA), ("accion", "verificar-codigo"));
        Assert.NotEqual(HttpStatusCode.Redirect, otro.StatusCode);

        var (ok, _) = await Enviar(c, pagina, ("codigo_email", codigoA), ("accion", "verificar-codigo"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var (repetido, _) = await Enviar(c, pagina, ("codigo_email", codigoA), ("accion", "verificar-codigo"));
        Assert.NotEqual(HttpStatusCode.Redirect, repetido.StatusCode);
    }

    [Fact]
    public async Task Hay_un_limite_de_codigos_por_mail()
    {
        var email = MailUnico();
        _app.Accesos.Sembrar(BaseUnica(), "B", "C6", email);
        var c = _app.Cliente();
        var (_, _, html) = await Abrir(c);
        string ultima = "";
        for (var i = 0; i <= OAuthEmailApp.Config.EmailMaxEnvios; i++)
            (_, ultima) = await Enviar(c, html, ("email", email), ("accion", "enviar-codigo"));
        Assert.Equal(OAuthEmailApp.Config.EmailMaxEnvios, _app.Mails.Para(email).Count);
        Assert.Contains("varios codigos", ultima);
    }

    [Fact]
    public async Task Con_varias_bases_se_elige_despues_del_codigo_y_el_ticket_es_de_un_solo_uso()
    {
        var email = MailUnico(); var b1 = BaseUnica(); var b2 = BaseUnica();
        _app.Accesos.Sembrar(b1, "Sucursal Norte", "C7", email);
        _app.Accesos.Sembrar(b2, "Sucursal Sur", "C7", email);
        var c = _app.Cliente();
        var (id, v, html) = await Abrir(c);

        var (_, codigoPag) = await Enviar(c, html, ("email", email), ("accion", "enviar-codigo"));
        Assert.DoesNotContain("Sucursal", codigoPag);   // antes de validar el codigo no se muestra ninguna base
        var (r, eleccion) = await Enviar(c, codigoPag, ("codigo_email", _app.Mails.UltimoCodigo(email)), ("accion", "verificar-codigo"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Sucursal Norte", eleccion);
        Assert.Contains("Sucursal Sur", eleccion);

        var (fin, _) = await Enviar(c, eleccion, ("base", b2.ToString()), ("accion", "elegir-base"));
        Assert.Equal(b2.ToString(), Claims((await Canjear(c, fin, id, v)).GetProperty("access_token").GetString()!)["base"]);

        var (otraVez, _) = await Enviar(c, eleccion, ("base", b1.ToString()), ("accion", "elegir-base"));
        Assert.NotEqual(HttpStatusCode.Redirect, otraVez.StatusCode);
    }

    [Fact]
    public async Task No_se_puede_elegir_una_base_a_la_que_el_mail_no_tiene_acceso()
    {
        var email = MailUnico(); var b1 = BaseUnica(); var b2 = BaseUnica(); var ajena = BaseUnica();
        _app.Accesos.Sembrar(b1, "Mia 1", "C8", email);
        _app.Accesos.Sembrar(b2, "Mia 2", "C8", email);
        _app.Accesos.Sembrar(ajena, "De otro cliente", "C9", MailUnico());
        var c = _app.Cliente();
        var (_, _, html) = await Abrir(c);
        var (_, eleccion) = await PedirYUsarCodigo(c, html, email);
        var (r, _) = await Enviar(c, eleccion, ("base", ajena.ToString()), ("accion", "elegir-base"));
        Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode);
    }

    [Fact]
    public async Task Si_se_quita_el_acceso_la_sesion_ya_no_se_renueva()
    {
        var email = MailUnico(); var idBase = BaseUnica();
        _app.Accesos.Sembrar(idBase, "B", "C10", email);
        var c = _app.Cliente();
        var (id, v, html) = await Abrir(c);
        var (r, _) = await PedirYUsarCodigo(c, html, email);
        var tokens = await Canjear(c, r, id, v);

        await _app.Accesos.QuitarAccesoAsync(email, idBase, default);
        var rr = await c.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.GetProperty("refresh_token").GetString()!, ["client_id"] = id }));
        Assert.Equal(HttpStatusCode.BadRequest, rr.StatusCode);
        Assert.Equal("invalid_grant", (await Json(rr)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task El_usuario_creado_por_mail_no_puede_entrar_con_clave()
    {
        var email = MailUnico();
        _app.Accesos.Sembrar(BaseUnica(), "B", "C11", email);
        var c = _app.Cliente();
        var (_, _, html) = await Abrir(c);
        await PedirYUsarCodigo(c, html, email);   // crea el registro tecnico del usuario

        var (_, _, html2) = await Abrir(c);
        foreach (var clave in new[] { "email-otp", "", "cualquier-clave-123" })
        {
            var (r, _) = await Enviar(c, html2, ("usuario", email), ("clave", clave), ("codigo", "123456"), ("accion", "autorizar"));
            Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode);
        }
    }
}

/// <summary>Aislamiento entre bases: la conexion sale del token, nunca cae a otra base.</summary>
public class ConexionPorBaseTests
{
    private static ConexionPorBaseResolver Resolver(MemoryAccesoStore accesos, ClaimsPrincipal? user)
    {
        var ctx = new DefaultHttpContext();
        if (user is not null) ctx.User = user;
        var protector = new SecretProtector(new AuthOptions(true, "Memory", "http://localhost", "/mcp",
            Path.Combine(Path.GetTempPath(), "mcp-test-key-" + Guid.NewGuid().ToString("N") + ".pem"), null, null, false, null, 10, 60, 10),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ContentRootPath = Path.GetTempPath() });
        return new ConexionPorBaseResolver(new HttpContextAccessor { HttpContext = ctx }, accesos, protector, "Server=por-defecto;Database=defecto",
            NullLogger<ConexionPorBaseResolver>.Instance);
    }

    private static ClaimsPrincipal Usuario(string? baseId, string metodo, string email) => new(new ClaimsIdentity(
        [new Claim("base", baseId ?? ""), new Claim("amr", metodo), new Claim("preferred_username", email)], "test"));

    [Fact]
    public async Task Sin_login_o_sin_base_usa_la_base_por_defecto()
    {
        var acc = new MemoryAccesoStore();
        Assert.Contains("por-defecto", await Resolver(acc, null).GetConnectionStringAsync(default));
        Assert.Contains("por-defecto", await Resolver(acc, Usuario(null, "pwd", "alberto")).GetConnectionStringAsync(default));
    }

    [Fact]
    public async Task Con_base_usa_el_servidor_y_la_base_de_ALFA_CENTRAL_y_el_login_de_solo_lectura()
    {
        var acc = new MemoryAccesoStore();
        acc.Sembrar(42, "Panaderia", "C1", "a@x.com", connectionString: "10.9.0.5\\SQL");
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(await Resolver(acc, Usuario("42", "email", "a@x.com")).GetConnectionStringAsync(default));
        Assert.Equal("10.9.0.5\\SQL", cs.DataSource);
        Assert.Equal("db42", cs.InitialCatalog);
        Assert.Equal("mcp_dir_ro", cs.UserID);
    }

    [Fact]
    public async Task Un_mail_no_puede_usar_una_base_a_la_que_no_tiene_acceso_ni_cae_a_la_base_por_defecto()
    {
        var acc = new MemoryAccesoStore();
        acc.Sembrar(1, "Del cliente A", "A", "a@x.com");
        acc.Sembrar(2, "Del cliente B", "B", "b@x.com");
        var ex = await Assert.ThrowsAsync<ToolUserException>(() => Resolver(acc, Usuario("2", "email", "a@x.com")).GetConnectionStringAsync(default));
        Assert.Contains("dado de baja", ex.Message);
        await Assert.ThrowsAsync<ToolUserException>(() => Resolver(acc, Usuario("999", "email", "a@x.com")).GetConnectionStringAsync(default));
        await Assert.ThrowsAsync<ToolUserException>(() => Resolver(acc, Usuario("abc", "email", "a@x.com")).GetConnectionStringAsync(default));
    }

    [Fact]
    public async Task Base_no_habilitada_o_acceso_quitado_cortan_la_consulta()
    {
        var acc = new MemoryAccesoStore();
        acc.Sembrar(7, "Apagada", "C", "a@x.com", habilitada: false);
        await Assert.ThrowsAsync<ToolUserException>(() => Resolver(acc, Usuario("7", "email", "a@x.com")).GetConnectionStringAsync(default));

        acc.Sembrar(8, "Activa", "C", "b@x.com");
        Assert.Contains("db8", await Resolver(acc, Usuario("8", "email", "b@x.com")).GetConnectionStringAsync(default));
        await acc.QuitarAccesoAsync("b@x.com", 8, default);
        await Assert.ThrowsAsync<ToolUserException>(() => Resolver(acc, Usuario("8", "email", "b@x.com")).GetConnectionStringAsync(default));
    }

    [Theory]
    [InlineData("  Ana@Cliente.COM ", "ana@cliente.com")]
    [InlineData("ana@cliente.com.ar", "ana@cliente.com.ar")]
    [InlineData("sin-arroba.com", null)]
    [InlineData("a@b", null)]
    [InlineData("dos@@x.com", null)]
    [InlineData("con espacio@x.com", null)]
    [InlineData("", null)]
    public void Normalizacion_de_mails(string entrada, string? esperado) => Assert.Equal(esperado, EmailNormalizado.Normalizar(entrada));
}
