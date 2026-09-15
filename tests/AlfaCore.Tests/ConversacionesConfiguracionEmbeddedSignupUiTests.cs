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
    public void PrimaryCtaLoadingFlag_NeverLightsUpDuringAPendingConnectionAction()
    {
        // Rediseño "detalle = número/conexión seleccionada" (ver auditoría Ministerio Vida de
        // Dios/AlfaNet): las viejas ramas globales Failed/Expired con su propio botón "Reintentar"
        // (Loading="@EmbeddedSignupRetryLoading") desaparecieron -- ahora reintentar/reconectar es la
        // acción primaria del AlfaFeedbackPanel de la conexión pendiente seleccionada, con su propio
        // Loading directo (_embeddedSignupBusy), nunca EmbeddedSignupPrimaryCtaLoading. La garantía que
        // importa se mantiene: el botón "Conectar WhatsApp" del estado vacío y la acción de una
        // conexión pendiente seleccionada nunca comparten el mismo flag visual de carga.
        var source = File.ReadAllText(FindPagePath());

        Assert.Contains("private bool EmbeddedSignupPrimaryCtaLoading => _embeddedSignupBusy && _embeddedSignupBusyOrigin == EmbeddedSignupBusyOrigin.PrimaryCta;", source, StringComparison.Ordinal);
        Assert.Contains("Loading=\"@EmbeddedSignupPrimaryCtaLoading\"", source, StringComparison.Ordinal);

        // El panel de la conexión pendiente usa el flag directo, no el de PrimaryCta.
        Assert.Contains("PrimaryActionLoading=\"@_embeddedSignupBusy\"", source, StringComparison.Ordinal);

        // RetryEmbeddedSignupAsync/ReconnectPendingConnectionAsync ponen el origen en None (nunca
        // PrimaryCta) -- si alguna vez usaran PrimaryCta por error, el botón "Conectar WhatsApp" del
        // estado vacío empezaría a animar durante un reintento no relacionado.
        var retryMethodStart = source.IndexOf("private async Task RetryEmbeddedSignupAsync(", StringComparison.Ordinal);
        Assert.True(retryMethodStart >= 0, "No se encontró RetryEmbeddedSignupAsync.");
        var retryMethodBody = ExtractMethodBody(source, retryMethodStart);
        Assert.Contains("_embeddedSignupBusyOrigin = EmbeddedSignupBusyOrigin.None;", retryMethodBody, StringComparison.Ordinal);

        var reconnectMethodStart = source.IndexOf("private async Task ReconnectPendingConnectionAsync(", StringComparison.Ordinal);
        Assert.True(reconnectMethodStart >= 0, "No se encontró ReconnectPendingConnectionAsync.");
        var reconnectMethodBody = ExtractMethodBody(source, reconnectMethodStart);
        Assert.Contains("_embeddedSignupBusyOrigin = EmbeddedSignupBusyOrigin.None;", reconnectMethodBody, StringComparison.Ordinal);

        // _embeddedSignupBusy sigue siendo la única fuente de verdad para Disabled del CTA vacío.
        var primaryCtaButtonBlock = ExtractTag(source, "<AlfaButton", "Loading=\"@EmbeddedSignupPrimaryCtaLoading\"");
        Assert.Contains("Disabled=\"@_embeddedSignupBusy\"", primaryCtaButtonBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedSignupFeedback_CanNeverRenderInsideTheOperationalNumberDetail()
    {
        var source = File.ReadAllText(FindPagePath());

        // MUY IMPORTANTE (auditoría UX post-Base4264, y confirmado de nuevo con evidencia real: Ministerio
        // Vida de Dios operativo + AlfaNet fallido -- el panel derecho mostraba "WhatsApp conectado"
        // mezclando el estado de OTRA conexión con la seleccionada). Ahora la rama del número operativo
        // seleccionado (SelectedApiNumero) es estructuralmente incapaz de referenciar _embeddedSignupFeedback
        // -- no hay ningún "if" que excluir: el campo simplemente no aparece en ese bloque de markup.
        var branchStart = source.IndexOf("else if (SelectedApiNumero is { } selectedNumero)", StringComparison.Ordinal);
        Assert.True(branchStart >= 0, "No se encontró la rama de detalle del número seleccionado.");
        // OJO: la condición del if ya contiene un "{ }" propio (el patrón "is { } selectedNumero"), así
        // que hay que arrancar a buscar la llave de apertura del BLOQUE recién después del ')' que
        // cierra la condición -- si no, ExtractMethodBody encuentra esa llave vacía del patrón primero.
        var blockOpenBrace = source.IndexOf('{', source.IndexOf(')', branchStart));
        var branchBody = ExtractMethodBody(source, blockOpenBrace);

        Assert.DoesNotContain("_embeddedSignupFeedback", branchBody, StringComparison.Ordinal);
        // En cambio, sí usa su propio estado con fuente de verdad (131031 derivado del último envío de
        // ESTE número) -- ya cubierto por Meta131031PanelTests, sólo se confirma acá que sigue presente.
        Assert.Contains("_selectedNumeroBlockedFeedback", branchBody, StringComparison.Ordinal);
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
