using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bot (2026-10-15): "Prueba Chat Web" abrió el ticket #79 porque el modelo contestó "parece que
/// estás probando" pero marcó DERIVA. El ticket automático queda sólo para consultas reales.
/// </summary>
public sealed class ConversacionesBotTicketConsultaTests
{
    [Theory]
    [InlineData("Prueba Chat Web")]
    [InlineData("prueba")]
    [InlineData("Test")]
    [InlineData("Hola, probando")]
    [InlineData("Esto es una prueba")]
    [InlineData("Hola")]
    [InlineData("Muchas gracias")]
    [InlineData("")]
    public void NoEsConsulta_NoGeneraTicket(string texto)
        => Assert.False(ConversacionesService.EsConsultaParaTicket(texto));

    [Theory]
    [InlineData("No me imprime la factura")]
    [InlineData("¿Cómo cargo un proveedor nuevo?")]
    [InlineData("Hago una prueba de factura y me da error")]
    [InlineData("No anda")]
    public void ConsultaReal_GeneraTicket(string texto)
        => Assert.True(ConversacionesService.EsConsultaParaTicket(texto));

    [Fact]
    public void ElBotNoDerivaNiAbreTicketAnteUnMensajeDePrueba()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        Assert.Contains("if (EsMensajeDePrueba(texto) && !string.Equals(result?.Tipo, \"RESUELVE\"", source, StringComparison.Ordinal);
        Assert.Contains("if (tipo == \"DERIVA\" && config.BotGeneraTicket && EsConsultaParaTicket(texto))", source, StringComparison.Ordinal);
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
