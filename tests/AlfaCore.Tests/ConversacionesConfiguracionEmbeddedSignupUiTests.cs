using System.Text.RegularExpressions;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// ConversacionesConfiguracion.razor es una página Blazor con ~20 servicios inyectados y sin arnés de
/// componentes (no hay bUnit en el repo); instrumentar uno sólo para estos fixes puntuales sería
/// desproporcionado. Sigue el mismo patrón ya establecido en WhatsAppEmbeddedSignupJavaScriptTests.cs /
/// EmbeddedSignupMultiTenantTests.cs: verificar el CONTRATO por texto fuente. Cubre los bugs de UI
/// confirmados en las auditorías Base4264: (1) _embeddedSignupStatus congelado tras timeout/cancelación,
/// (2) _embeddedSignupBusy compartido causando dos spinners simultáneos, (3) 2026-09-14, IdOnboarding
/// d23ddd26-393b-4334-a0b9-87b1bb6e2098: el watchdog visual de 90s cancelaba en el servidor y podía
/// ganarle la carrera a un callback real de autorización que llegaba alrededor de ese mismo instante.
/// </summary>
public sealed class ConversacionesConfiguracionEmbeddedSignupUiTests
{
    [Fact]
    public void ExplicitCancellationPath_RefreshesEmbeddedSignupStatus_InsteadOfStayingStale()
    {
        var source = File.ReadAllText(FindPagePath());

        // Antes del fix, PersistEmbeddedSignupCancellationAsync sólo hacía StateHasChanged(), nunca
        // volvía a pedir el estado: _embeddedSignupStatus quedaba con el snapshot de la carga de
        // página anterior a este intento (p. ej. el mensaje de un EXPIRED viejo). Ahora debe refrescar
        // vía RefreshWhatsAppConnectionsAsync (que internamente vuelve a llamar GetLatestStatusForBaseAsync).
        var methodStart = source.IndexOf("private async Task PersistEmbeddedSignupCancellationAsync()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró PersistEmbeddedSignupCancellationAsync.");
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("await EmbeddedSignup.HandleCancellationAsync(id, idBase, _embeddedSignupState, usuario)", methodBody, StringComparison.Ordinal);
        Assert.Contains("await RefreshWhatsAppConnectionsAsync(idBase)", methodBody, StringComparison.Ordinal);
        Assert.Contains("await InvokeAsync(StateHasChanged)", methodBody, StringComparison.Ordinal);

        // Sólo la cancelación explícita del SDK de Meta (usuario cerró el popup, Meta lo detectó)
        // sigue pasando por este método -- el watchdog visual ya NO (ver test de abajo).
        Assert.Contains("return PersistEmbeddedSignupCancellationAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualWatchdogTimeout_NeverCancelsOnServer_OnlyRefreshes()
    {
        var source = File.ReadAllText(FindPagePath());

        // REGRESIÓN Base4264 (2026-09-14, IdOnboarding d23ddd26-393b-4334-a0b9-87b1bb6e2098): el
        // watchdog visual de 90s llamaba a PersistEmbeddedSignupCancellationAsync() al vencer, que
        // escribe CANCELLED en el servidor -- un timer del browser no tiene forma de saber si Meta
        // está por terminar de verdad (~90.273s en el caso real). Si el callback real llegaba
        // alrededor de ese mismo instante, competía por el mismo StateHash de un solo uso contra este
        // timeout puramente visual, y si el timeout ganaba la autorización real quedaba CANCELLED sin
        // ningún error visible. El watchdog ahora es puramente informativo: nunca debe llamar
        // PersistEmbeddedSignupCancellationAsync ni HandleCancellationAsync -- sólo refresca el estado
        // real (que ya expira solo por FechaExpiracionUtc, el dominio, no por este timer de UI).
        var methodStart = source.IndexOf("private async Task WatchEmbeddedSignupAuthorizationTimeoutAsync(CancellationToken token)", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró WatchEmbeddedSignupAuthorizationTimeoutAsync.");
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.DoesNotContain("PersistEmbeddedSignupCancellationAsync", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleCancellationAsync", methodBody, StringComparison.Ordinal);
        Assert.Contains("await RefreshWhatsAppConnectionsAsync(idBase)", methodBody, StringComparison.Ordinal);

        // "await PersistEmbeddedSignupCancellationAsync();" (con await, la forma que usaba el
        // watchdog) ya no debe existir en NINGÚN lado del archivo -- sólo queda la forma
        // "return PersistEmbeddedSignupCancellationAsync();" de la cancelación explícita del SDK.
        Assert.DoesNotContain("await PersistEmbeddedSignupCancellationAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryCtaAndRetryButtons_HaveIndependentLoadingFlags()
    {
        var source = File.ReadAllText(FindPagePath());

        // Existen dos getters de Loading distintos, cada uno atado a un origen distinto del mismo
        // _embeddedSignupBusy compartido -- ya no un único flag animando dos botones a la vez.
        Assert.Contains("private enum EmbeddedSignupBusyOrigin { None, PrimaryCta, Retry }", source, StringComparison.Ordinal);
        Assert.Contains("private bool EmbeddedSignupPrimaryCtaLoading => _embeddedSignupBusy && _embeddedSignupBusyOrigin == EmbeddedSignupBusyOrigin.PrimaryCta;", source, StringComparison.Ordinal);
        Assert.Contains("private bool EmbeddedSignupRetryLoading => _embeddedSignupBusy && _embeddedSignupBusyOrigin == EmbeddedSignupBusyOrigin.Retry;", source, StringComparison.Ordinal);

        // El CTA principal (lista vacía de WhatsApp) usa el flag de PrimaryCta, no el _embeddedSignupBusy
        // crudo -- y el "Reintentar" de los paneles Failed/Expired usa el de Retry. Sin este split,
        // ambos "latían" juntos ante cualquier _embeddedSignupBusy=true (bug confirmado en Base4264).
        Assert.Contains("Loading=\"@EmbeddedSignupPrimaryCtaLoading\"", source, StringComparison.Ordinal);
        Assert.Contains("Loading=\"@EmbeddedSignupRetryLoading\"", source, StringComparison.Ordinal);

        var retryLoadingCount = Regex.Matches(source, Regex.Escape("Loading=\"@EmbeddedSignupRetryLoading\"")).Count;
        Assert.Equal(2, retryLoadingCount); // panel Failed + panel Expired

        // Todo punto que apaga _embeddedSignupBusy también debe limpiar el origen -- si no, un origen
        // viejo (p. ej. Retry de un intento anterior) podría "filtrarse" y prender el spinner
        // equivocado en la próxima acción no relacionada.
        var busyFalseCount = Regex.Matches(source, @"_embeddedSignupBusy = false;").Count;
        var originResetCount = Regex.Matches(source, @"_embeddedSignupBusyOrigin = EmbeddedSignupBusyOrigin\.None;").Count;
        Assert.True(originResetCount >= busyFalseCount,
            $"Cada '_embeddedSignupBusy = false' debería tener su '_embeddedSignupBusyOrigin = None' acompañante ({originResetCount} resets vs {busyFalseCount} apagados de busy).");

        // OpenWhatsAppOnboardingModeDialog acepta el origen explícitamente (no hay forma de saber qué
        // botón abrió el diálogo sin este parámetro).
        Assert.Contains("private Task OpenWhatsAppOnboardingModeDialog(EmbeddedSignupBusyOrigin origin = EmbeddedSignupBusyOrigin.PrimaryCta)", source, StringComparison.Ordinal);
        Assert.Contains("_embeddedSignupBusyOrigin = origin;", source, StringComparison.Ordinal);
        Assert.Contains("OpenWhatsAppOnboardingModeDialog(EmbeddedSignupBusyOrigin.Retry)", source, StringComparison.Ordinal);

        // _embeddedSignupBusy sigue siendo la única fuente de verdad para Disabled (nada de esto debilita
        // la protección contra doble-submit; sólo separa qué botón ANIMA su spinner).
        var primaryCtaButtonBlock = ExtractTag(source, "<AlfaButton", "Loading=\"@EmbeddedSignupPrimaryCtaLoading\"");
        Assert.Contains("Disabled=\"@_embeddedSignupBusy\"", primaryCtaButtonBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedSignupFeedback_NeverRendersOverConnectedState()
    {
        var source = File.ReadAllText(FindPagePath());

        // MUY IMPORTANTE (auditoría UX post-Base4264): el feedback de un intento NUEVO (p. ej. "Conectar
        // otro WhatsApp" que falla) no debe aparecer pegado debajo de la tarjeta verde "WhatsApp
        // conectado" de un número YA operativo, como si ambos fueran el mismo objeto. El render de
        // _embeddedSignupFeedback debe excluir explícitamente el estado Connected.
        var renderIndex = source.IndexOf("_embeddedSignupFeedback is not null", StringComparison.Ordinal);
        Assert.True(renderIndex >= 0, "No se encontró el render condicionado de _embeddedSignupFeedback.");
        var conditionLine = source[renderIndex..source.IndexOf('\n', renderIndex)];
        Assert.Contains("EmbeddedSignupUiState != AlfaCore.Models.WhatsAppEmbeddedConnectionUiState.Connected", conditionLine, StringComparison.Ordinal);
    }

    [Fact]
    public void StartingNewEmbeddedSignupAttempt_ClearsStaleFeedbackFromPreviousAttempt()
    {
        var source = File.ReadAllText(FindPagePath());

        // Al arrancar un intento nuevo (StartEmbeddedSignupAsync), _embeddedSignupFeedback se
        // sobrescribe incondicionalmente con un mensaje "InProgress" propio de este intento -- así un
        // error de un intento anterior no sigue visible mientras corre uno nuevo.
        var methodStart = source.IndexOf("private async Task StartEmbeddedSignupAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró StartEmbeddedSignupAsync.");
        var methodBody = ExtractMethodBody(source, methodStart);
        Assert.Contains("_embeddedSignupFeedback = AppUiMessage.InProgress(", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteEmbeddedSignupAuthorization_NeverUsesBlindCatch()
    {
        var source = File.ReadAllText(FindPagePath());

        // Regresión Base4264: el catch ciego ("catch { }") de este método ocultaba la excepción real
        // de HandleAuthorizationCallbackAsync (la carrera watchdog/callback incluida). Ahora debe
        // capturar la excepción, loguearla sanitizada y clasificarla -- nunca descartarla en silencio.
        var methodStart = source.IndexOf("public async Task CompleteEmbeddedSignupAuthorization(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró CompleteEmbeddedSignupAuthorization.");
        var methodBody = ExtractMethodBody(source, source.IndexOf('{', methodStart));

        Assert.Contains("catch (Exception ex)", methodBody, StringComparison.Ordinal);
        Assert.Contains("AppEvents.LogErrorAsync(", methodBody, StringComparison.Ordinal);
        Assert.Contains("ClassifyEmbeddedSignupAuthorizationError(ex, incident, HostEnvironment.IsProduction())", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroAssignedUsers_RendersBigWarningPanel_NotSmallGrayText()
    {
        var source = File.ReadAllText(FindPagePath());

        // Fase 11 de la auditoría UX: un número conectado con 0 usuarios asignados puede ser
        // invisible/inoperable en la bandeja para el usuario actual. Antes se mostraba como
        // <p class="wa-field-note">Sin usuarios asignados.</p> -- texto gris chico, indistinguible de
        // cualquier otro dato secundario, fácil de confundir con un problema de Meta/desconexión.
        var methodStart = source.IndexOf("private static RenderFragment ApiUsers(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró ApiUsers.");
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.DoesNotContain("wa-field-note\">Sin usuarios asignados", methodBody, StringComparison.Ordinal);
        Assert.Contains("AlfaFeedbackPanel", methodBody, StringComparison.Ordinal);
        Assert.Contains("AppUiMessage.Warning(", methodBody, StringComparison.Ordinal);
        Assert.Contains("sin usuarios asignados", methodBody, StringComparison.OrdinalIgnoreCase);
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

    private static string ExtractTag(string source, string tagStartNeedle, string containingNeedle)
    {
        var needleIndex = source.IndexOf(containingNeedle, StringComparison.Ordinal);
        Assert.True(needleIndex >= 0, $"No se encontró '{containingNeedle}'.");
        var tagStart = source.LastIndexOf(tagStartNeedle, needleIndex, StringComparison.Ordinal);
        Assert.True(tagStart >= 0, $"No se encontró la apertura de tag '{tagStartNeedle}' antes de '{containingNeedle}'.");
        var tagEnd = source.IndexOf('>', needleIndex);
        Assert.True(tagEnd >= 0);
        return source[tagStart..(tagEnd + 1)];
    }

    private static string FindPagePath() => FindRepoFile("src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor");

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("No se encontró ConversacionesConfiguracion.razor desde el directorio de pruebas.");
    }
}
