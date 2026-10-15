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
        Assert.Contains("if (tipo == \"DERIVA\" && config.BotGeneraTicket && !omitirTicket && EsConsultaParaTicket(texto))", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pudieron ver algo?")]
    [InlineData("si,ayudame con lo que pedimos ayer")]
    [InlineData("¿Novedades?")]
    [InlineData("Hola, alguna novedad?")]
    [InlineData("sigo esperando")]
    [InlineData("hola,pudiste ver algo?")]
    public void PedidoDeNovedades_SeReconoce(string texto)
        => Assert.True(ConversacionesService.PideNovedades(texto));

    [Theory]
    [InlineData("No me imprime la factura")]
    [InlineData("buen día")]
    [InlineData("Quiero dar de alta un proveedor nuevo y no encuentro la opción en el menú de compras")]
    public void ConsultaNueva_NoEsPedidoDeNovedades(string texto)
        => Assert.False(ConversacionesService.PideNovedades(texto));

    [Fact]
    public void PedidoDeNovedades_RespondeSinNombrarANadieYSinTicket()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        Assert.DoesNotContain("{", ConversacionesService.RespuestaSeguimiento, StringComparison.Ordinal);
        Assert.Contains("tema pendiente", ConversacionesService.RespuestaSeguimiento, StringComparison.Ordinal);
        Assert.Matches(@"await ResponderSeguimientoAsync\(idConversacion, pendiente, token\)\.ConfigureAwait\(false\);\s*return;", source);
        Assert.Contains("!omitirTicket && EsConsultaParaTicket(texto)", source, StringComparison.Ordinal);
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
