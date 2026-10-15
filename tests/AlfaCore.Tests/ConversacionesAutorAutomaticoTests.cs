using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Mensajes automáticos (2026-10-15): se grababan sin autor (UsuarioAutor/SistemaAutor tienen FK a
/// TA_USUARIOS), así que el tope de respuestas del asistente, la contención repetida, el aviso previo
/// al cierre por inactividad, el fuera de horario y el SLA no los reconocían. Ahora van en
/// CONV_MENSAJES.AutorAutomatico.
/// </summary>
public sealed class ConversacionesAutorAutomaticoTests
{
    [Theory]
    [InlineData("BOT", "BOT")]
    [InlineData("bot", "BOT")]
    [InlineData("AUTOMATIZACION", "AUTOMATIZACION")]
    [InlineData("AUTOCIERRE_AVISO", "AUTOCIERRE_AVISO")]
    [InlineData("SLA", "SLA")]
    [InlineData("CN000pr", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SoloLasAutomatizacionesSeMarcan(string? sistema, string? esperado)
        => Assert.Equal(esperado, ConversacionesService.ResolveAutorAutomatico(sistema));

    [Fact]
    public void NingunaConsultaReconoceAutomaticosSoloPorSistemaAutor()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        Assert.DoesNotContain("ISNULL(SistemaAutor, '') = N'BOT'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AND SistemaAutor = N'AUTOMATIZACION'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISNULL(n.SistemaAutor, '') = N'SLA'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISNULL(mm.SistemaAutor, '') AS SistemaAutor", source, StringComparison.Ordinal);
        Assert.Contains("@AutorAutomatico", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ElScriptAgregaLaColumna()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "App_Data", "updates",
            "2026-10-15-001__conversaciones_mensajes_autor_automatico.sql"));
        Assert.Contains("ADD AutorAutomatico nvarchar(30) NULL", script, StringComparison.Ordinal);
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
