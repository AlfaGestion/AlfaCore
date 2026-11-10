using Xunit;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;

namespace AlfaCore.Tests;

public class MetaCanalesSocialesTests
{
    [Fact]
    public void EsquemaCentral_coincide_con_el_script_de_referencia()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "src", "AlfaCore", "AlfaCore.csproj")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new InvalidOperationException("No se encontró el repositorio.");
        var referencia = File.ReadAllText(Path.Combine(dir, "docs", "base-datos", "sql-referencia", "2026-11-05-001__alfa_central_conv_canales_sociales.sql"))
            .Replace("\r\n", "\n").TrimEnd('\n');
        Assert.Equal(referencia, CentralCanalesSocialesSchema.Script.Replace("\r\n", "\n").TrimEnd('\n'));
        Assert.DoesNotMatch(@"(?im)^\s*GO\s*$", CentralCanalesSocialesSchema.Script);
    }

    [Fact]
    public void Webhook_separa_los_entry_por_cuenta()
    {
        var root = JsonNode.Parse("""
            {"object":"instagram","entry":[
              {"id":"111","time":1,"messaging":[{"sender":{"id":"a"},"message":{"mid":"m1","text":"hola"}}]},
              {"id":222,"time":2,"messaging":[{"sender":{"id":"b"},"message":{"mid":"m2","text":"buenas"}}]},
              {"id":"111","time":3,"messaging":[{"sender":{"id":"c"},"message":{"mid":"m3","text":"otro"}}]},
              {"time":4}
            ]}
            """);

        var grupos = MetaCanalesSocialesWebhook.SepararPorCuenta(root);

        Assert.Equal(2, grupos.Count);
        var cuenta111 = Assert.Single(grupos, g => g.CuentaId == "111");
        Assert.Equal("instagram", cuenta111.Payload["object"]!.GetValue<string>());
        Assert.Equal(2, cuenta111.Payload["entry"]!.AsArray().Count);
        var cuenta222 = Assert.Single(grupos, g => g.CuentaId == "222");
        Assert.Single(cuenta222.Payload["entry"]!.AsArray());
    }

    [Fact]
    public void Webhook_sin_entry_no_devuelve_grupos()
    {
        Assert.Empty(MetaCanalesSocialesWebhook.SepararPorCuenta(JsonNode.Parse("""{"object":"page"}""")));
        Assert.Empty(MetaCanalesSocialesWebhook.SepararPorCuenta(null));
    }

    [Fact]
    public void Paginas_de_Facebook_se_leen_con_token_foto_y_permiso_de_mensajes()
    {
        using var doc = JsonDocument.Parse("""
            {"data":[
              {"id":"10","name":"Negocio","username":"negocio","access_token":"t1","tasks":["ANALYZE","MESSAGING"],"picture":{"data":{"url":"https://x/p.jpg"}}},
              {"id":"20","name":"Solo anuncios","access_token":"t2","tasks":["ADVERTISE","ANALYZE"]},
              {"id":"30","name":"Sin token"}
            ]}
            """);

        var paginas = MetaCanalesSocialesClient.ParseFacebookPages(doc.RootElement);

        Assert.Equal(2, paginas.Count);
        Assert.True(paginas[0].PuedeMensajes);
        Assert.Equal("https://x/p.jpg", paginas[0].FotoUrl);
        Assert.Equal("negocio", paginas[0].Usuario);
        Assert.False(paginas[1].PuedeMensajes);
    }

    [Fact]
    public void Token_con_vencimiento_acepta_numero_o_texto()
    {
        using var numero = JsonDocument.Parse("""{"access_token":"abc","expires_in":5184000}""");
        var (token, vence) = MetaCanalesSocialesClient.ReadTokenWithExpiry(numero.RootElement);
        Assert.Equal("abc", token);
        Assert.NotNull(vence);
        Assert.InRange((vence!.Value - DateTime.UtcNow).TotalDays, 59, 61);

        using var texto = JsonDocument.Parse("""{"access_token":"abc","expires_in":"3600"}""");
        Assert.NotNull(MetaCanalesSocialesClient.ReadTokenWithExpiry(texto.RootElement).VenceUtc);

        using var sin = JsonDocument.Parse("""{"access_token":"abc"}""");
        Assert.Null(MetaCanalesSocialesClient.ReadTokenWithExpiry(sin.RootElement).VenceUtc);
    }

    [Theory]
    [InlineData("""{"error":{"message":"Invalid OAuth access token.","code":190}}""", "Invalid OAuth access token. (código 190)")]
    [InlineData("""{"error":{"message":"x","error_user_msg":"Mensaje para el usuario"}}""", "Mensaje para el usuario")]
    [InlineData("""{"error_type":"OAuthException","code":400,"error_message":"Invalid redirect_uri"}""", "Invalid redirect_uri")]
    [InlineData("no es json", "")]
    public void Error_de_Meta_se_lee_sin_detalles_del_pedido(string body, string esperado)
        => Assert.Equal(esperado, MetaCanalesSocialesClient.ReadMetaError(body));

    [Theory]
    [InlineData("/abc/4264/conversaciones/configuracion?x=1", "/abc/4264/conversaciones/configuracion")]
    [InlineData("https://malo.com/robo", "/conversaciones/configuracion")]
    [InlineData("//malo.com/robo", "/conversaciones/configuracion")]
    [InlineData("/\\malo.com", "/conversaciones/configuracion")]
    [InlineData("", "/conversaciones/configuracion")]
    public void La_vuelta_solo_acepta_rutas_del_propio_sitio(string entrada, string esperado)
        => Assert.Equal(esperado, MetaCanalesSocialesService.SanitizeReturnPath(entrada));

    [Fact]
    public void Url_de_vuelta_incluye_canal_y_conexion()
    {
        var pendiente = new MetaConexionPendiente { Id = "abc_123", Canal = MetaCanalSocial.Facebook, ReturnPath = "/w/1/conversaciones/configuracion" };
        Assert.Equal("/w/1/conversaciones/configuracion?seccion=canales&subseccion=facebook&meta_conexion=abc_123",
            MetaCanalesSocialesService.BuildReturnUrl(pendiente));
    }

    [Fact]
    public void Url_de_redireccion_usa_el_host_actual_y_exige_https()
    {
        var servicio = CrearServicio(new MetaCanalesSocialesOptions());
        Assert.Equal("https://alfacentral.ddns.net/api/conversaciones/meta/oauth/instagram/callback",
            servicio.BuildRedirectUri(MetaCanalSocial.Instagram, "https://alfacentral.ddns.net/"));
        Assert.Throws<MetaCanalSocialException>(() => servicio.BuildRedirectUri(MetaCanalSocial.Instagram, "http://alfacentral.ddns.net/"));

        var fijo = CrearServicio(new MetaCanalesSocialesOptions { CallbackBaseUrl = "https://fijo.example.com" });
        Assert.Equal("https://fijo.example.com/api/conversaciones/meta/oauth/facebook/callback",
            fijo.BuildRedirectUri(MetaCanalSocial.Facebook, "https://otro.example.com/"));
    }

    [Fact]
    public void Url_de_Instagram_lleva_permisos_y_state()
    {
        var client = new MetaCanalesSocialesClient(null!,
            Options.Create(new MetaCanalesSocialesOptions { InstagramAppId = "999", InstagramScopes = "instagram_business_basic, instagram_business_manage_messages" }),
            Options.Create(new WhatsAppEmbeddedSignupOptions()));
        var url = client.BuildInstagramAuthorizeUrl("https://h/cb", "st");
        Assert.StartsWith("https://www.instagram.com/oauth/authorize?", url);
        Assert.Contains("client_id=999", url);
        Assert.Contains("scope=instagram_business_basic%2Cinstagram_business_manage_messages", url);
        Assert.Contains("state=st", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fh%2Fcb", url);
    }

    [Fact]
    public void Facebook_usa_la_app_de_WhatsApp_si_no_tiene_una_propia()
    {
        var whatsApp = new WhatsAppEmbeddedSignupOptions { AppId = "1436", AppSecret = "s" };
        var opciones = new MetaCanalesSocialesOptions { Enabled = true };
        Assert.Equal("1436", opciones.ResolveFacebookAppId(whatsApp));
        Assert.True(opciones.FacebookListo(whatsApp));
        Assert.False(opciones.InstagramListo);
    }

    [Fact]
    public void Conexion_pendiente_vence_y_se_puede_quitar()
    {
        var pendientes = new MetaConexionesPendientes();
        var item = pendientes.Crear(MetaCanalSocial.Instagram, 4264, "eve", "https://h/cb", "/x");
        Assert.Same(item, pendientes.Get(item.Id));
        Assert.Null(pendientes.Get("otro"));
        Assert.Null(pendientes.Get(null));
        pendientes.Quitar(item.Id);
        Assert.Null(pendientes.Get(item.Id));
    }

    private static MetaCanalesSocialesService CrearServicio(MetaCanalesSocialesOptions opciones)
        => new(null!, null!, new MetaConexionesPendientes(), null!, null!, null!, null!,
            Options.Create(opciones), Options.Create(new WhatsAppEmbeddedSignupOptions()));
}
