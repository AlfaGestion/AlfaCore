using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Enseñarle al asistente (2026-10-15): el equipo corrige una respuesta y, ante una consulta parecida,
/// el asistente la sigue con prioridad (casi igual: responde directo con lo enseñado).
/// </summary>
public sealed class ConversacionAsistenteCorreccionesTests
{
    private static double Parecido(string consulta, string enseñada)
        => ConversacionAsistenteCorreccionesService.Parecido(
            ConversacionAsistenteCorreccionesService.PalabrasClave(consulta),
            ConversacionAsistenteCorreccionesService.PalabrasClave(enseñada));

    [Theory]
    [InlineData("No me imprime la factura", "no imprime factura")]
    [InlineData("¿Cómo cargo un proveedor nuevo?", "como cargo proveedor nuevo")]
    [InlineData("Hola, no me imprimen las facturas", "No me imprime la factura")]
    public void MismaPreguntaConOtrasPalabras_EsCasiIgual(string consulta, string enseñada)
        => Assert.True(Parecido(consulta, enseñada) >= ConversacionAsistenteCorreccionesService.ParecidoDirecto);

    [Theory]
    [InlineData("¿Cuál es el horario de atención?", "No me imprime la factura")]
    [InlineData("Quiero dar de alta un usuario nuevo en el sistema para mi empleado de la sucursal", "factura")]
    public void PreguntaDistinta_NoSeUsa(string consulta, string enseñada)
        => Assert.True(Parecido(consulta, enseñada) < ConversacionAsistenteCorreccionesService.ParecidoMinimo);

    [Fact]
    public void PreguntaParecidaPeroMasLarga_SeUsaComoGuia()
    {
        var p = Parecido("Buen día, desde ayer no me imprime la factura electrónica en la caja 2", "no imprime factura");
        Assert.InRange(p, ConversacionAsistenteCorreccionesService.ParecidoMinimo, ConversacionAsistenteCorreccionesService.ParecidoDirecto);
    }

    [Fact]
    public void LasCorreccionesVanPrimeroEnLaInformacionDelAsistente()
    {
        var correcciones = new[]
        {
            new AsistenteCorreccionCoincidencia(new AsistenteCorreccionDto
            {
                PreguntaCliente = "no imprime factura",
                Accion = AsistenteCorreccionAcciones.Responder,
                RespuestaCorrecta = "Revisá que la impresora fiscal esté encendida."
            }, 0.6),
            new AsistenteCorreccionCoincidencia(new AsistenteCorreccionDto
            {
                PreguntaCliente = "quiero la baja",
                Accion = AsistenteCorreccionAcciones.Derivar
            }, 0.6)
        };

        var info = ConversacionesService.AgregarCorreccionesAlContexto("Somos Alfa.", correcciones);

        Assert.StartsWith("CORRECCIONES DEL EQUIPO", info, StringComparison.Ordinal);
        Assert.Contains("Revisá que la impresora fiscal esté encendida.", info, StringComparison.Ordinal);
        Assert.Contains("pasalo a una persona (tipo DERIVA)", info, StringComparison.Ordinal);
        Assert.EndsWith("Somos Alfa.", info, StringComparison.Ordinal);
        Assert.Equal("Somos Alfa.", ConversacionesService.AgregarCorreccionesAlContexto("Somos Alfa.", []));
    }

    [Theory]
    [InlineData("derivar", AsistenteCorreccionAcciones.Derivar)]
    [InlineData("SIN_TICKET", AsistenteCorreccionAcciones.SinTicket)]
    [InlineData("cualquier cosa", AsistenteCorreccionAcciones.Responder)]
    [InlineData(null, AsistenteCorreccionAcciones.Responder)]
    public void AccionSeNormaliza(string? accion, string esperada)
        => Assert.Equal(esperada, AsistenteCorreccionAcciones.Normalizar(accion));

    [Fact]
    public void ElScriptCreaLaTabla()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        var script = File.ReadAllText(Path.Combine(current!.FullName, "src", "AlfaCore", "App_Data", "updates",
            "2026-10-15-005__conversaciones_asistente_correcciones.sql"));
        Assert.Contains("CREATE TABLE dbo.CONV_ASISTENTE_CORRECCIONES", script, StringComparison.Ordinal);
    }
}

public sealed class ConversacionAsistenteCorreccionesAvisoTests
{
    private static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0);

    [Fact]
    public void CorreccionesPendientes_AvisanAlAdministradorYLlevanALoQueAprendio()
    {
        var avisos = CentroAvisosService.ConstruirAvisos([], [], [], "admin", new HashSet<string>(), Ahora,
            correcciones: new AvisoCorreccionesFuente(3, 42, Ahora.AddMinutes(-5)));

        var aviso = Assert.Single(avisos, a => a.Tipo == AvisoTipos.AsistenteAprendizaje);
        Assert.Equal("corr:42", aviso.Clave);
        Assert.Contains("3 correcciones", aviso.Detalle, StringComparison.Ordinal);
        Assert.Equal("/conversaciones/configuracion?seccion=asistente-ia&subseccion=aprendido", aviso.Ruta);
    }

    [Fact]
    public void SinPendientesOYaLeido_NoAvisa()
    {
        Assert.DoesNotContain(CentroAvisosService.ConstruirAvisos([], [], [], "admin", new HashSet<string>(), Ahora),
            a => a.Tipo == AvisoTipos.AsistenteAprendizaje);
        Assert.DoesNotContain(CentroAvisosService.ConstruirAvisos([], [], [], "admin", new HashSet<string> { "corr:42" }, Ahora,
                correcciones: new AvisoCorreccionesFuente(1, 42, Ahora)),
            a => a.Tipo == AvisoTipos.AsistenteAprendizaje && !a.Leido);
    }

    [Fact]
    public void RespuestaBuena_VaComoEjemploYNuncaSeRespondeDirecto()
    {
        var info = ConversacionesService.AgregarCorreccionesAlContexto(string.Empty,
        [
            new AsistenteCorreccionCoincidencia(new AsistenteCorreccionDto
            {
                PreguntaCliente = "horario",
                Accion = AsistenteCorreccionAcciones.Buena,
                RespuestaCorrecta = "Atendemos de 9 a 18."
            }, 1.0)
        ]);
        Assert.Contains("marcó como correcta esta respuesta (usala de ejemplo", info, StringComparison.Ordinal);

        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        Assert.Contains("c.Correccion.Accion is AsistenteCorreccionAcciones.Responder or AsistenteCorreccionAcciones.Derivar", source, StringComparison.Ordinal);
        Assert.Equal(AsistenteCorreccionAcciones.Buena, AsistenteCorreccionAcciones.Normalizar("buena"));
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
