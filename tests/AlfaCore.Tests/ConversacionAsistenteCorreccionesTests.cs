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
            "2026-10-15-002__conversaciones_asistente_correcciones.sql"));
        Assert.Contains("CREATE TABLE dbo.CONV_ASISTENTE_CORRECCIONES", script, StringComparison.Ordinal);
    }
}
