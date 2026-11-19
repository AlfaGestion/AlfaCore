using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class TicketInformesTests(ITestOutputHelper output)
{
    [Fact]
    public void SanitizerQuitaScriptsEventosYUrlsPeligrosas()
    {
        var html = InformeHtmlSanitizer.Limpiar("""
            <p onclick="robar()">Hola <script>alert(1)</script><strong>mundo</strong></p>
            <a href="javascript:alert(1)">mal</a> <a href="https://alfa.com">bien</a>
            <img src=x onerror=alert(1)><iframe src="https://x"></iframe><style>p{}</style>
            <!-- comentario --><unknown>texto suelto</unknown>
            """);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("onerror", html);
        Assert.DoesNotContain("javascript", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("iframe", html);
        Assert.DoesNotContain("comentario", html);
        Assert.Contains("<p>Hola <strong>mundo</strong></p>", html);
        Assert.Contains("href=\"https://alfa.com\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains("texto suelto", html);
    }

    [Fact]
    public void SanitizerConservaTablasBannersYCierraEtiquetas()
    {
        var html = InformeHtmlSanitizer.Limpiar("<table><tr><td colspan=\"2\" style=\"x\">A</td></tr></table><div class=\"ticket-editor-banner ticket-editor-banner--warning otra\"><i class=\"bi bi-exclamation-triangle-fill ticket-editor-banner__icon\"></i><p>Ojo <b>abierto");
        Assert.Contains("<td colspan=\"2\">A</td>", html);
        Assert.Contains("class=\"ticket-editor-banner ticket-editor-banner--warning\"", html);
        Assert.Contains("bi-exclamation-triangle-fill", html);
        Assert.EndsWith("</b></p></div>", html);
        Assert.Equal("<p>1 &lt; 2 &amp; 3</p>", InformeHtmlSanitizer.Limpiar("<p>1 < 2 &amp; 3</p>"));
    }

    [Fact]
    public void TextoPlanoYHtmlSeConviertenBien()
    {
        Assert.Equal("<p>Hola<br>chau</p><p>Otro &lt;b&gt;</p>", InformeHtmlSanitizer.TextoAHtml("Hola\nchau\n\nOtro <b>"));
        Assert.Equal("Título\n- uno\n- dos", InformeHtmlSanitizer.ATexto("<h2>Título</h2><ul><li>uno</li><li>dos</li></ul>"));
        Assert.True(InformeHtmlSanitizer.PareceHtml("<p>x</p>"));
        Assert.False(InformeHtmlSanitizer.PareceHtml("a < b y c > d"));
    }

    [Theory]
    [InlineData("```html\n<p>x</p>\n```", "<p>x</p>")]
    [InlineData("<p>x</p>", "<p>x</p>")]
    public void QuitaElCercoDeCodigoDeLaIa(string entrada, string esperado) => Assert.Equal(esperado, TicketInformesService.QuitarCercoDeCodigo(entrada));

    [Fact]
    public void PlantillaYContenidoParaIaUsanLosDatosDelTicket()
    {
        var c = new TicketInformeContexto
        {
            Numero = 181, Titulo = "Precios", Cliente = "FERNANDEZ", Estado = "En proceso", FechaHoraAlta = new(2026, 11, 19, 8, 0, 0),
            Descripcion = "Linea 1\nLinea 2",
            Actividad = [(new(2026, 11, 19, 9, 0, 0), "NOTA", "Se restauraron los precios", "Soporte")],
            Mensajes = [(new(2026, 11, 19, 9, 5, 0), "INBOUND", "¿Ya está?")],
            Partes = [(new(2026, 11, 19), 90, "Auditoría")]
        };
        var plantilla = TicketInformesService.Plantilla(c);
        Assert.Contains("<p>Linea 1<br>Linea 2</p>", plantilla);
        Assert.Contains("<li>Se restauraron los precios</li>", plantilla);
        Assert.Contains("<li>Auditoría</li>", plantilla);
        var contenido = TicketInformesService.ContenidoParaIa(c, "tono tranquilo");
        Assert.Contains("Ticket #181: Precios", contenido);
        Assert.Contains("Indicaciones de quien pide el informe: tono tranquilo", contenido);
        Assert.Contains("Cliente: ¿Ya está?", contenido);
        Assert.Contains("90 min: Auditoría", contenido);
        Assert.Contains("No inventes", TicketInformesService.PromptSistema);
    }

    [Fact]
    public void ElTipoDeLinkInformeEntraEnLaRestriccionCentral()
    {
        Assert.Contains(PublicLinkTipos.Informe, PublicLinkTipos.All);
        Assert.Contains("'INFORME'", PublicLinkTipos.CheckConstraintScript);
        Assert.True(PublicLinkTipos.Informe.Length <= 10);
    }

    /// <summary>
    /// Recorrido completo contra una base de soporte de pruebas con el update 2026-11-19-061: plantilla, guardar, publicar
    /// (link en memoria), lectura pública, apertura y baja. Deja la base como estaba.
    /// </summary>
    [SoportePruebaFact]
    public async Task RecorridoCompletoEnBaseDePrueba()
    {
        var cadena = Environment.GetEnvironmentVariable("ALFACORE_TEST_SOPORTE_SQL")!;
        long idTicket;
        await using (var cn = new SqlConnection(cadena))
            idTicket = await cn.ExecuteScalarAsync<long>("SELECT TOP (1) IdTicket FROM dbo.TICK_TICKETS ORDER BY IdTicket DESC;");

        var links = new LinksEnMemoria();
        var svc = new TicketInformesService(new ConfigurationBuilder().Build(), Sesion(cadena), links, Ia(null));
        var destino = new TicketInformeDestino("PRUEBA", 4283, "https://alfa.test/");
        var plantilla = await svc.PlantillaAsync(idTicket);
        Assert.Contains("<h2>Qué pasó</h2>", plantilla);

        var id = await svc.GuardarAsync(new TicketInformeGuardarRequest { IdTicket = idTicket, Titulo = "Prueba automática", Html = plantilla + "<script>x</script>", Usuario = "TEST" });
        try
        {
            var lista = await svc.ListarAsync(idTicket);
            var informe = Assert.Single(lista, x => x.IdInforme == id);
            Assert.DoesNotContain("<script", informe.Html);
            Assert.False(informe.Publicado);
            Assert.Null(await svc.ObtenerPublicoAsync(id));

            var url = await svc.PublicarAsync(id, "TEST", destino);
            Assert.StartsWith("https://alfa.test/PRUEBA/informe/", url);
            Assert.Equal(url, await svc.ObtenerLinkAsync(id, destino));
            var publico = await svc.ObtenerPublicoAsync(id);
            Assert.NotNull(publico);
            Assert.Equal("Prueba automática", publico!.Titulo);
            await svc.RegistrarVistaAsync(id, "test");
            Assert.Equal(1, (await svc.ListarAsync(idTicket)).Single(x => x.IdInforme == id).Vistas);

            await svc.DespublicarAsync(id, "TEST", destino);
            Assert.Null(await svc.ObtenerLinkAsync(id, destino));
            Assert.Null(await svc.ObtenerPublicoAsync(id));
            output.WriteLine($"ticket {idTicket} informe {id} ok");
        }
        finally
        {
            await using var cn = new SqlConnection(cadena);
            await cn.ExecuteAsync("""
                DELETE FROM dbo.TICK_INFORME_VISTAS WHERE IdInforme = @Id;
                DELETE FROM dbo.TICK_INFORMES WHERE IdInforme = @Id;
                DELETE FROM dbo.TICK_ACTIVIDAD WHERE IdTicket = @IdTicket AND TipoActividad = 'INFORME' AND Usuario = 'TEST';
                """, new { Id = id, IdTicket = idTicket });
        }
    }

    private static ISessionService Sesion(string cadena)
    {
        var s = DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)s).Handler = (m, _) => m.Name == nameof(ISessionService.GetConnectionString) ? cadena : null;
        return s;
    }

    private static IConversacionAnalisisService Ia(string? respuesta)
    {
        var s = DispatchProxy.Create<IConversacionAnalisisService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)s).Handler = (m, _) => m.Name switch
        {
            "get_IsConfigured" => respuesta is not null,
            nameof(IConversacionAnalisisService.GenerarTextoAsync) => Task.FromResult(respuesta),
            _ => null
        };
        return s;
    }

    private sealed class LinksEnMemoria : ICentralPublicLinkService
    {
        private readonly List<PublicLinkDto> _links = [];
        public Task<PublicLinkDto?> ResolveAsync(string idWeb, string tipo, string routeSegment, CancellationToken ct = default)
            => Task.FromResult(_links.FirstOrDefault(x => x.Activo && x.IdWeb == idWeb && x.Tipo == tipo && routeSegment.EndsWith(x.Token, StringComparison.Ordinal)));
        public Task<PublicLinkDto?> TryGetExistingAsync(string idWeb, int idBase, string tipo, int idReferencia, CancellationToken ct = default)
            => Task.FromResult(_links.FirstOrDefault(x => x.Activo && x.IdWeb == idWeb && x.IdBase == idBase && x.Tipo == tipo && x.IdReferencia == idReferencia));
        public async Task<PublicLinkDto> GetOrCreateAsync(string idWeb, int idBase, string tipo, int idReferencia, string? nombreParaSlug, CancellationToken ct = default)
        {
            if (await TryGetExistingAsync(idWeb, idBase, tipo, idReferencia, ct) is { } existente) return existente;
            var nuevo = new PublicLinkDto { IdPublicLink = _links.Count + 1, IdWeb = idWeb, IdBase = idBase, Tipo = tipo, IdReferencia = idReferencia, Token = Guid.NewGuid().ToString("N")[..24], Slug = "informe", Activo = true };
            _links.Add(nuevo);
            return nuevo;
        }
        public Task<IReadOnlyDictionary<int, PublicLinkDto>> GetOrCreateManyAsync(string idWeb, int idBase, string tipo, IReadOnlyList<(int IdReferencia, string? Nombre)> referencias, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RevokeAsync(int idPublicLink, CancellationToken ct = default)
        {
            var i = _links.FindIndex(x => x.IdPublicLink == idPublicLink);
            if (i >= 0) _links[i] = new PublicLinkDto { IdPublicLink = _links[i].IdPublicLink, IdWeb = _links[i].IdWeb, IdBase = _links[i].IdBase, Tipo = _links[i].Tipo, IdReferencia = _links[i].IdReferencia, Token = _links[i].Token, Activo = false };
            return Task.CompletedTask;
        }
        public Task<PublicLinkDto> RegenerateAsync(int idPublicLink, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

public sealed class SoportePruebaFactAttribute : FactAttribute
{
    public SoportePruebaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SOPORTE_SQL")))
            Skip = "Definí ALFACORE_TEST_SOPORTE_SQL (base de soporte de pruebas con el update 2026-11-19-061).";
    }
}
