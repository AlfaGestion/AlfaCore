using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 2 de la auditoría UX: bug real reportado "No se pudo enviar la reacción por WhatsApp."
/// Trazado a ConversacionesService.SendReactionAsync/SendReactionToWhatsAppAsync -- confirmado por
/// código, sin asumir la causa: el catch envolvía SIEMPRE la excepción real en un
/// InvalidOperationException genérico con esa frase fija, sin importar si la causa era un rechazo de
/// Graph, un problema de red, o cualquier otra cosa. Se corrigió para no tragar la excepción real (ver
/// commit), igual que ya se había hecho para plantillas y Embedded Signup. Además: Meta acepta un
/// mensaje de reacción con emoji vacío para QUITAR una reacción previa, pero NormalizeReactionEmoji/
/// SendReactionAsync lo rechazaban incondicionalmente -- no había forma de quitar una reacción, sólo de
/// reemplazarla por otra. Se agregó RemoveReaction como flag explícito.
/// </summary>
public sealed class WhatsAppReactionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void SendReactionAsync_NeverWrapsTheRealFailureInAHardcodedGenericMessage()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<ConversacionMessageResultDto> SendReactionAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró SendReactionAsync.");
        var methodBody = ExtractMethodBody(source, methodStart);

        // Regresión confirmada: esto ANTES envolvía cualquier excepción en
        // `new InvalidOperationException("No se pudo enviar la reacción por WhatsApp.", ex)` -- la UI
        // nunca podía ver la causa real. Ahora debe re-lanzar (throw;) para que ExecuteLoggedAsync la
        // envuelva de forma uniforme (AppUserFacingException con la excepción real como InnerException,
        // recuperable por el clasificador).
        Assert.DoesNotContain("throw new InvalidOperationException(\"No se pudo enviar la reacción por WhatsApp.\", ex)", methodBody, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", methodBody, StringComparison.Ordinal);

        var catchIndex = methodBody.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var braceIndex = methodBody.IndexOf('{', catchIndex);
        var closeIndex = methodBody.IndexOf('}', braceIndex);
        var catchBlock = methodBody[braceIndex..(closeIndex + 1)];
        Assert.Contains("throw;", catchBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void SendReactionToWhatsAppAsync_UsesHttpRequestException_ConsistentWithTemplatesAndText()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("private async Task<WhatsAppSendResult> SendReactionToWhatsAppAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);

        // Antes lanzaba InvalidOperationException para un rechazo de Graph -- inconsistente con
        // SendTemplateToWhatsAppAsync/SendToWhatsAppAsync (que ya usaban HttpRequestException), y ese
        // tipo colisionaba con las reglas de negocio propias (también InvalidOperationException) al
        // intentar distinguir "mensaje de regla de negocio, ya limpio" de "falla real de Graph".
        Assert.Contains("throw new HttpRequestException($\"Meta devolvi\\u00f3 {(int)response.StatusCode}: {responseBody}\")", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAReaction_IsExplicitAndDoesNotWeakenTheEmptyEmojiGuard()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<ConversacionMessageResultDto> SendReactionAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("request.RemoveReaction ? string.Empty : NormalizeReactionEmoji(request.Emoji)", methodBody, StringComparison.Ordinal);
        Assert.Contains("if (!request.RemoveReaction && string.IsNullOrWhiteSpace(emoji))", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ReactionModel_HasExplicitRemoveFlag()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Models", "ConversacionesModels.cs"));
        var classStart = source.IndexOf("public sealed class ConversacionReaccionRequest", StringComparison.Ordinal);
        Assert.True(classStart >= 0);
        var classBody = ExtractMethodBody(source, classStart);
        Assert.Contains("public bool RemoveReaction { get; set; }", classBody, StringComparison.Ordinal);
    }

    [Fact]
    public void CanReactToMessage_AlreadyHidesTheActionWhenReactionIsNotPossible()
    {
        // Confirmado por código, no reescrito: CanReactToMessage ya excluye canal no-WhatsApp, ventana
        // vencida, mensajes salientes (no se reacciona a los propios) y mensajes sin wamid -- el botón
        // de reaccionar directamente no se renderiza en esos casos, en vez de mostrarse y fallar con un
        // error genérico al hacer click.
        var source = ReadPageSource();
        var propertyStart = source.IndexOf("private bool CanReactToMessage(ConversacionMensajeDto message)", StringComparison.Ordinal);
        Assert.True(propertyStart >= 0);
        var propertyBody = source[propertyStart..source.IndexOf(';', propertyStart)];

        Assert.Contains("IsWhatsAppConversation", propertyBody, StringComparison.Ordinal);
        Assert.Contains("!IsWhatsAppWindowExpired", propertyBody, StringComparison.Ordinal);
        Assert.Contains("IsIncomingMessage(message)", propertyBody, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(message.WhatsAppMessageId)", propertyBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ReactionPicker_TogglesToRemove_WhenClickingTheAlreadyActiveEmoji()
    {
        var source = ReadPageSource();
        Assert.Contains("var isActive = string.Equals(emoji, myActiveReaction, StringComparison.Ordinal);", source, StringComparison.Ordinal);
        Assert.Contains("await (isActive ? RemoveReactionAsync(reactionMessageId) : SendReactionAsync(reactionMessageId, reactionEmoji))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReactionSendCatch_ClassifiesRealCause_InsteadOfGenericExceptionMessage()
    {
        var source = ReadPageSource();
        var methodStart = source.IndexOf("private async Task SendOrRemoveReactionAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.DoesNotContain("_actionError = ex.Message;", methodBody, StringComparison.Ordinal);
        Assert.Contains("WhatsAppOutboundErrorClassifier.ClassifyDeliverySendException(causeException)", methodBody, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, int methodStart)
    {
        var openBrace = source.IndexOf('{', methodStart);
        Assert.True(openBrace >= 0);
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[methodStart..(i + 1)];
            }
        }
        throw new InvalidOperationException("No se pudo delimitar el cuerpo del método.");
    }

    private static string ReadServiceSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

    private static string ReadPageSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

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
