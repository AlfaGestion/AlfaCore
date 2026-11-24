using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WebChatContextoSoporteTests
{
    private const string Contexto = "ALFA GESTION | CLI:112010760 | ANIMAL PRO | USR:Administrador | BASE:ANIMALPRO_BOLSON";

    [Theory]
    [InlineData("CLI", "112010760")]
    [InlineData("cli", "112010760")]
    [InlineData("USR", "Administrador")]
    [InlineData("BASE", "ANIMALPRO_BOLSON")]
    public void LeeCadaCampoDelContexto(string clave, string esperado)
        => Assert.Equal(esperado, WebChatContextoSoporte.Campo(Contexto, clave));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALFA GESTION | ANIMAL PRO")]
    [InlineData("Consulta desde la web")]
    public void SinCampoDevuelveNull(string? contexto)
        => Assert.Null(WebChatContextoSoporte.Campo(contexto, "CLI"));

    [Fact]
    public void ToleraEspaciosAlrededor()
        => Assert.Equal("00123", WebChatContextoSoporte.Campo("CLI :  00123  |USR:Ana", "CLI"));
}
