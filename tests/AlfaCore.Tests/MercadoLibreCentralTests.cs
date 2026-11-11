using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public class MercadoLibreCentralTests
{
    private static readonly MercadoLibreCentralOptions Central = new()
    {
        Enabled = true,
        ClientId = "6296866990333297",
        ClientSecret = "secreto-central"
    };

    [Fact]
    public void PostVenta_formato_nuevo_toma_el_mensaje_pedido_y_el_pack()
    {
        var body = """
            {"paging":{"total":2},"messages":[
              {"id":"otro","from":{"user_id":"1"},"text":"no","message_resources":[{"id":"999","name":"packs"}]},
              {"id":"abc123","from":{"user_id":"555"},"to":{"user_id":"777"},"text":"¿Cuándo llega?",
               "message_date":{"received":"2026-11-11T10:00:00.000-03:00","created":"2026-11-11T09:59:00.000-03:00"},
               "message_resources":[{"id":"2000001","name":"packs"},{"id":"777","name":"sellers"}]}
            ]}
            """;

        var mensaje = ConversacionesService.ParseMercadoLibrePostVenta(body, "abc123");

        Assert.NotNull(mensaje);
        Assert.Equal("abc123", mensaje!.MessageId);
        Assert.Equal("2000001", mensaje.PackId);
        Assert.Equal("555", mensaje.FromUserId);
        Assert.Equal("¿Cuándo llega?", mensaje.Texto);
        Assert.NotEqual(default, mensaje.Fecha);
    }

    [Fact]
    public void PostVenta_objeto_directo_con_texto_plain_y_orden()
    {
        var body = """{"id":"x1","from":{"user_id":"5"},"text":{"plain":"Hola"},"message_resources":[{"id":"42","name":"orders"}]}""";
        var mensaje = ConversacionesService.ParseMercadoLibrePostVenta(body, "x1");
        Assert.Equal("42", mensaje!.PackId);
        Assert.Equal("Hola", mensaje.Texto);
    }

    [Fact]
    public void PostVenta_solo_adjunto_avisa_que_hay_un_archivo()
    {
        var body = """{"id":"x2","from":{"user_id":"5"},"text":"","message_attachments":[{"original_filename":"factura.pdf"}],"message_resources":[{"id":"42","name":"packs"}]}""";
        var mensaje = ConversacionesService.ParseMercadoLibrePostVenta(body, "x2");
        Assert.Contains("factura.pdf", mensaje!.Texto);
        Assert.Single(mensaje.Adjuntos);
    }

    [Theory]
    [InlineData("/messages/0b1c2d", "0b1c2d")]
    [InlineData("0b1c2d", "0b1c2d")]
    [InlineData("/messages/0b1c2d?tag=post_sale", "0b1c2d")]
    [InlineData("", "")]
    public void Id_del_mensaje_sale_del_resource(string resource, string esperado)
        => Assert.Equal(esperado, ConversacionesService.ExtractMercadoLibreMessageId(resource));

    [Fact]
    public void Texto_largo_se_parte_en_mensajes_de_hasta_350_cortando_en_espacios()
    {
        var texto = string.Join(' ', Enumerable.Repeat("palabra", 120)); // ~959 caracteres
        var partes = ConversacionesService.PartirTextoMercadoLibre(texto, ConversacionesService.MercadoLibreMensajeMaximo);

        Assert.True(partes.Count >= 3);
        Assert.All(partes, p => Assert.True(p.Length <= 350));
        Assert.All(partes, p => Assert.False(p.StartsWith(' ') || p.EndsWith(' ')));
        Assert.Equal(texto.Replace(" ", string.Empty), string.Concat(partes).Replace(" ", string.Empty));
        Assert.Single(ConversacionesService.PartirTextoMercadoLibre("corto", 350));
        Assert.Empty(ConversacionesService.PartirTextoMercadoLibre("   ", 350));
    }

    [Fact]
    public void Pack_se_reconoce_por_el_prefijo()
    {
        Assert.True(ConversacionesService.IsMercadoLibrePackConversation("pack-2000001"));
        Assert.False(ConversacionesService.IsMercadoLibrePackConversation("13254455"));
        Assert.False(ConversacionesService.IsMercadoLibrePackConversation(null));
    }

    [Fact]
    public void Aviso_central_lee_vendedor_app_y_topico()
    {
        var aviso = MercadoLibreCentralWebhook.Leer("""{"resource":"/questions/1","user_id":123456,"topic":"questions","application_id":6296866990333297,"attempts":1}""");
        Assert.NotNull(aviso);
        Assert.Equal("123456", aviso!.UserId);
        Assert.Equal("6296866990333297", aviso.ApplicationId);
        Assert.True(MercadoLibreCentralWebhook.TopicoSoportado(aviso.Topic));
        Assert.True(MercadoLibreCentralWebhook.TopicoSoportado("messages"));
        Assert.False(MercadoLibreCentralWebhook.TopicoSoportado("items"));
        Assert.Null(MercadoLibreCentralWebhook.Leer("no es json"));
    }

    [Fact]
    public void Credencial_central_se_completa_solo_para_bases_conectadas_con_la_app_central()
    {
        var central = new ConversacionMercadoLibreConfigDto { ClientId = Central.ClientId, AccessToken = "t" };
        ConversacionesConfigService.AplicarCredencialesCentralesMercadoLibre(central, Central);
        Assert.Equal("secreto-central", central.ClientSecret);
        Assert.True(central.CredencialesCentrales);
        Assert.True(central.IsConfiguredForApi);

        var propia = new ConversacionMercadoLibreConfigDto { ClientId = "otra-app", ClientSecret = "propio" };
        ConversacionesConfigService.AplicarCredencialesCentralesMercadoLibre(propia, Central);
        Assert.Equal("propio", propia.ClientSecret);
        Assert.False(propia.CredencialesCentrales);

        var sinApp = new ConversacionMercadoLibreConfigDto { ClientId = "otra-app" };
        ConversacionesConfigService.AplicarCredencialesCentralesMercadoLibre(sinApp, Central);
        Assert.Equal(string.Empty, sinApp.ClientSecret);

        var apagado = new ConversacionMercadoLibreConfigDto { ClientId = Central.ClientId };
        ConversacionesConfigService.AplicarCredencialesCentralesMercadoLibre(apagado, new MercadoLibreCentralOptions { ClientId = Central.ClientId, ClientSecret = "x" });
        Assert.Equal(string.Empty, apagado.ClientSecret);
    }

    [Fact]
    public void Url_de_autorizacion_y_tokens()
    {
        var client = new MercadoLibreCentralClient(null!, Options.Create(Central));
        var url = client.BuildAuthorizeUrl("https://h/api/conversaciones/mercadolibre/central/oauth/callback", "st");
        Assert.StartsWith("https://auth.mercadolibre.com.ar/authorization?response_type=code", url);
        Assert.Contains("client_id=6296866990333297", url);
        Assert.Contains("state=st", url);
        Assert.DoesNotContain("secreto", url);

        using var doc = JsonDocument.Parse("""{"access_token":"APP_USR-1","refresh_token":"TG-2","expires_in":21600,"user_id":123}""");
        var tokens = MercadoLibreCentralClient.ReadTokens(doc.RootElement);
        Assert.Equal("APP_USR-1", tokens.AccessToken);
        Assert.Equal("TG-2", tokens.RefreshToken);
        Assert.Equal("123", tokens.UserId);
        Assert.InRange((tokens.VenceUtc!.Value - DateTime.UtcNow).TotalHours, 5.9, 6.1);

        Assert.Equal("invalid_grant", MercadoLibreCentralClient.ReadError("""{"error":"invalid_grant","status":400}"""));
        Assert.Equal("Invalid token (unauthorized)", MercadoLibreCentralClient.ReadError("""{"message":"Invalid token","error":"unauthorized"}"""));
    }

    [Fact]
    public void Redireccion_de_Mercado_Libre_exige_https()
    {
        var servicio = new MercadoLibreConexionService(null!, null!, new MetaConexionesPendientes(), null!, null!, null!, null!, Options.Create(Central));
        Assert.Equal("https://alfanetweb.ddns.net/api/conversaciones/mercadolibre/central/oauth/callback",
            servicio.BuildRedirectUri("https://alfanetweb.ddns.net/"));
        Assert.Throws<MetaCanalSocialException>(() => servicio.BuildRedirectUri("http://localhost:5055/"));
    }

    [Fact]
    public void La_conexion_de_Meta_no_acepta_Mercado_Libre()
    {
        var meta = new MetaCanalesSocialesService(null!, null!, new MetaConexionesPendientes(), null!, null!, null!, null!,
            Options.Create(new MetaCanalesSocialesOptions()), Options.Create(new WhatsAppEmbeddedSignupOptions()));
        Assert.Throws<MetaCanalSocialException>(() => meta.IniciarConexion(MetaCanalSocial.MercadoLibre, 1, "eve", "https://h/", "/x"));
    }

    [Fact]
    public void Titulo_de_la_conexion_de_Mercado_Libre_es_el_apodo()
    {
        var conexion = new MetaCanalConexionDto { Canal = MetaCanalSocial.MercadoLibre, CuentaUsuario = "ALFANET.AR", CuentaNombre = "Alfa Net" };
        Assert.Equal("ALFANET.AR", conexion.Titulo);
    }
}
