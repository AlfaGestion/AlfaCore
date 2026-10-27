using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Audio que se cortaba al llegar otro mensaje (2026-10-20): al refrescar se piden los últimos N
/// mensajes, en conversaciones de 60+ el más viejo sale de la lista y, sin @key, Blazor reciclaba
/// los elementos por posición: el &lt;audio&gt; que sonaba pasaba a ser de otro mensaje y se cortaba.
/// </summary>
public sealed class ConversacionesAudioNoSeCortaTests
{
    private static readonly string Source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

    [Fact]
    public void CadaMensajeYCadaAudioTienenKeyEstable()
    {
        Assert.Contains("<div @key=\"message.IdMensaje\"\n                                     id=\"@GetMessageDomId(message.IdMensaje)\"",
            Source.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("<div @key=\"message.IdMensaje\" class=\"conversation-event-divider\">", Source, StringComparison.Ordinal);
        Assert.Contains("<div @key=\"adj.IdAdjunto\" class=\"message-attachment message-attachment--audio\">", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void LosMensajesVisiblesNoRepitenId()
        => Assert.Contains(".DistinctBy(x => x.IdMensaje)", Source, StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        return current!.FullName;
    }
}
