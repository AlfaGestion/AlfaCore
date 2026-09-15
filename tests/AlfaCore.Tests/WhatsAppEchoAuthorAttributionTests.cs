using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 7 de la auditoría UX: bug real confirmado por código (no backend, sólo atribución de
/// autor). Un mensaje reflejado de smb_message_echoes (Origen=WHATSAPP_BUSINESS_APP en InsertMessageAsync)
/// se inserta con UsuarioAutor/SistemaAutor/IdTecnicoAutor vacíos a propósito -- correcto, porque
/// AlfaCore genuinamente no sabe qué persona del negocio lo mandó desde el teléfono. Pero
/// GetMessageAuthor (Conversaciones.razor) no distinguía ese caso del de un mensaje SALIENTE normal:
/// ambos caían al mismo fallback "Equipo Alfa", atribuyendo incorrectamente al soporte de AlfaCore un
/// mensaje que en realidad mandó el propio negocio desde la app de WhatsApp Business. Además, Origen
/// se persistía (columna real, self-healing) pero nunca se leía de vuelta hacia el cliente -- la UI no
/// tenía ninguna forma de saber que un mensaje era un echo.
/// </summary>
public sealed class WhatsAppEchoAuthorAttributionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ConversacionMensajeDto_ExposesOrigen_ToTheClient()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Models", "ConversacionesModels.cs"));
        var classStart = source.IndexOf("public sealed class ConversacionMensajeDto", StringComparison.Ordinal);
        Assert.True(classStart >= 0);
        var classBody = ExtractBody(source, classStart);
        Assert.Contains("public string Origen { get; set; } = string.Empty;", classBody, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMessagesPageAsync_SelectsAndMapsOrigen_SoTheClientActuallyReceivesIt()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var methodStart = source.IndexOf("public Task<ConversacionMensajesPaginaDto> GetMessagesPageAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractBody(source, methodStart);

        Assert.Contains("ISNULL(m.Origen, '') AS Origen", methodBody, StringComparison.Ordinal);
        Assert.Contains("page.Origen", methodBody, StringComparison.Ordinal);
        Assert.Contains("Origen = GetString(rd, 17)", methodBody, StringComparison.Ordinal);
        // Si esta base tenant nunca recibió un mensaje de history/echo, la columna Origen podría no
        // existir todavía -- sin este self-heal, el SELECT que ahora la lee rompería la carga de
        // mensajes completa para esos tenants.
        Assert.Contains("await EnsureMensajeOrigenColumnAsync(cn, token);", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMessageAuthor_NeverAttributesAnEchoMessageToAlfaCoreTeam()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));
        var methodStart = source.IndexOf("private string GetMessageAuthor(ConversacionMensajeDto message)", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractBody(source, methodStart, until: ';');

        Assert.Contains("WHATSAPP_BUSINESS_APP", methodBody, StringComparison.Ordinal);
        Assert.Contains("\"WhatsApp Business\"", methodBody, StringComparison.Ordinal);
        // Un autor real de AlfaCore (si por algún motivo lo hubiera) sigue teniendo prioridad sobre la
        // etiqueta genérica -- FirstNonEmpty(TecnicoAutorNombre, UsuarioAutor, "WhatsApp Business").
        Assert.Contains("FirstNonEmpty(message.TecnicoAutorNombre, message.UsuarioAutor, \"WhatsApp Business\")", methodBody, StringComparison.Ordinal);
    }

    private static string ExtractBody(string source, int start, char until = '}')
    {
        if (until == ';')
        {
            var semicolon = source.IndexOf(';', start);
            Assert.True(semicolon >= 0);
            return source[start..(semicolon + 1)];
        }

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0);
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }
        throw new InvalidOperationException("No se pudo delimitar el cuerpo.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se pudo ubicar la raíz del repositorio (AlfaCore.sln) desde el directorio de pruebas.");
    }
}
