using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Encuesta de satisfacción (2026-10-19): al cerrar una conversación de WhatsApp se le manda al cliente
/// una pregunta con 3 botones (😀 Muy bien / 😐 Regular / 😞 Mal). La respuesta vuelve como un
/// button_reply con id "csat:{IdEncuesta}:{valor}".
/// </summary>
public sealed class ConversacionesEncuestaSatisfaccionTests
{
    private static readonly DateTime Ahora = new(2026, 10, 19, 12, 0, 0);

    [Theory]
    [InlineData("csat:15:3", 15L, 3)]
    [InlineData("csat:7:1", 7L, 1)]
    [InlineData("csat:123456:2", 123456L, 2)]
    public void ParseaElBotonDeLaEncuesta(string id, long encuesta, int calificacion)
    {
        var resultado = ConversacionesService.ParsearBotonEncuesta(id);
        Assert.NotNull(resultado);
        Assert.Equal(encuesta, resultado!.Value.IdEncuesta);
        Assert.Equal(calificacion, resultado.Value.Calificacion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("csat:15:4")]
    [InlineData("csat:15:0")]
    [InlineData("csat:abc:3")]
    [InlineData("csat:15")]
    [InlineData("otro:15:3")]
    public void IgnoraBotonesQueNoSonDeLaEncuesta(string? id)
        => Assert.Null(ConversacionesService.ParsearBotonEncuesta(id));

    [Fact]
    public void ExtraeElIdDelBotonDelWebhookDeMeta()
    {
        const string raw = """
            {"from":"5491100000000","id":"wamid.X","type":"interactive",
             "interactive":{"type":"button_reply","button_reply":{"id":"csat:42:1","title":"😞 Mal"}}}
            """;
        Assert.Equal("csat:42:1", ConversacionesService.ExtraerIdBotonRespuesta(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"type\":\"text\",\"text\":{\"body\":\"hola\"}}")]
    [InlineData("button_reply no es json")]
    public void SinBotonDevuelveVacio(string? raw)
        => Assert.Equal(string.Empty, ConversacionesService.ExtraerIdBotonRespuesta(raw));

    [Fact]
    public void LosBotonesEntranEnElLimiteDeMeta()
    {
        Assert.Equal(3, ConversacionEncuestaCalificaciones.Botones.Count);
        Assert.All(ConversacionEncuestaCalificaciones.Botones, b => Assert.True(b.Titulo.Length <= 20, b.Titulo));
    }

    [Fact]
    public void CalificacionMalaAvisaEnLaCampana()
    {
        var avisos = CentroAvisosService.ConstruirAvisos([], [], ["T1"], "evelyn", new HashSet<string>(), Ahora,
            calificaciones:
            [
                new AvisoCalificacionFuente(9, 33, "Franco", "Tardaron mucho", Ahora.AddDays(-1)),
                new AvisoCalificacionFuente(8, 32, "Viejo", string.Empty, Ahora.AddDays(-30))
            ]);

        var aviso = Assert.Single(avisos, a => a.Tipo == AvisoTipos.CalificacionMala);
        Assert.Equal("csat:9", aviso.Clave);
        Assert.Equal("Calificación mala: Franco", aviso.Titulo);
        Assert.Contains("Tardaron mucho", aviso.Detalle, StringComparison.Ordinal);
        Assert.Equal("/conversaciones?id=33", aviso.Ruta);
    }

    [Theory]
    [InlineData(2026, 10, 19, 2026, 10, 19)] // lunes
    [InlineData(2026, 10, 25, 2026, 10, 19)] // domingo
    [InlineData(2026, 10, 22, 2026, 10, 19)]
    public void LaSemanaEmpiezaElLunes(int y, int m, int d, int ey, int em, int ed)
        => Assert.Equal(new DateTime(ey, em, ed), ConversacionesSatisfaccionService.InicioSemana(new DateTime(y, m, d, 18, 30, 0)));

    [Fact]
    public void PorcentajesDelInforme()
    {
        var informe = new SatisfaccionInformeDto { Enviadas = 8, Respondidas = 7, MuyBien = 4, Regular = 2, Mal = 1 };
        Assert.Equal(87.5, informe.PorcentajeRespuesta);
        Assert.Equal(57.1, informe.PorcentajeSatisfechos);
        Assert.Equal(2.43, informe.Promedio);
        Assert.Equal(0, new SatisfaccionInformeDto().PorcentajeSatisfechos);
    }

    [Fact]
    public void ElCierreDisparaLaEncuestaYLaRespuestaNoReabre()
    {
        var root = FindRepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        Assert.Contains("TryEnviarEncuestaAlCerrarAsync(", service, StringComparison.Ordinal);
        Assert.Contains("TryProcesarRespuestaEncuestaAsync(", service, StringComparison.Ordinal);
        // La respuesta a la encuesta llega con la conversación ya cerrada: no tiene que reabrirla.
        Assert.Contains("AND ISNULL(m.Origen, N'') <> N'ENCUESTA'", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ElScriptCreaLaTabla()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "App_Data", "updates",
            "2026-10-19-001__conversaciones_encuestas_satisfaccion.sql"));
        Assert.Contains("CREATE TABLE dbo.CONV_ENCUESTAS", script, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.CONV_ENCUESTAS', N'U') IS NULL", script, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        return current!.FullName;
    }
}
