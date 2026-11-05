using AlfaCore.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// API de resumen diario (/api/brief/*): lo que sale del sistema no puede llevar contraseñas ni
/// números de tarjeta/CBU, y el token se valida sin depender de que la API esté mal configurada.
/// </summary>
public sealed class BriefApiTests
{
    [Theory]
    [InlineData("mi contraseña es Hunter2xx por favor", "Hunter2xx")]
    [InlineData("la clave: abc12345 sirve", "abc12345")]
    [InlineData("Password=Sup3rS3creta", "Sup3rS3creta")]
    public void Limpiar_OcultaContrasenias(string texto, string secreto)
    {
        var salida = BriefTextSanitizer.Limpiar(texto, 300);

        Assert.DoesNotContain(secreto, salida);
        Assert.Contains("[OCULTO]", salida);
    }

    [Theory]
    [InlineData("tarjeta 4509 9535 6623 3704 vence 12/27", "4509")]
    [InlineData("CBU 0170099220000067797370 gracias", "0170099220000067797370")]
    public void Limpiar_OcultaTarjetasYCbu(string texto, string dato)
    {
        var salida = BriefTextSanitizer.Limpiar(texto, 300);

        Assert.DoesNotContain(dato, salida);
        Assert.Contains("[NRO OCULTO]", salida);
    }

    [Fact]
    public void Limpiar_TruncaYColapsaEspacios()
    {
        var salida = BriefTextSanitizer.Limpiar("hola\r\n\r\n   que tal " + new string('x', 400), 300);

        Assert.True(salida.Length <= 300);
        Assert.StartsWith("hola que tal", salida);
        Assert.EndsWith("…", salida);
    }

    [Fact]
    public void Limpiar_OcultaAntesDeTruncar()
    {
        // El secreto queda justo en el límite: si se truncara primero se filtraría un pedazo.
        var texto = new string('a', 290) + " clave 12345678901";
        var salida = BriefTextSanitizer.Limpiar(texto, 300);

        Assert.DoesNotContain("12345", salida);
    }

    [Fact]
    public void ToIso_UsaHoraArgentina()
    {
        Assert.Equal("2026-11-05T08:30:00-03:00", BriefService.ToIso(new DateTime(2026, 11, 5, 8, 30, 0)));
        Assert.Null(BriefService.ToIso(null));
    }

    [Fact]
    public void SinTokenOSinConexion_NoQuedaHabilitado()
    {
        Assert.False(Crear(null, null).EstaHabilitado);
        Assert.False(Crear("secreto", null).EstaHabilitado);
        Assert.False(Crear(null, "Server=x").EstaHabilitado);
        Assert.True(Crear("secreto", "Server=x").EstaHabilitado);
    }

    [Fact]
    public void TokenValido_ExigeCoincidenciaExacta_YRechazaVacio()
    {
        var brief = Crear("secreto", "Server=x");

        Assert.True(brief.TokenValido("secreto"));
        Assert.False(brief.TokenValido("Secreto"));
        Assert.False(brief.TokenValido(""));
        Assert.False(brief.TokenValido(null));
        Assert.False(Crear(null, "Server=x").TokenValido(""));
    }

    private static BriefService Crear(string? token, string? conexion)
    {
        var valores = new Dictionary<string, string?>();
        if (token is not null) valores["Brief:Token"] = token;
        if (conexion is not null) valores["Brief:ConnectionString"] = conexion;
        return new BriefService(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());
    }
}
