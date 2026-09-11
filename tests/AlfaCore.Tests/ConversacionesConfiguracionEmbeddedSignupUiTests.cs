using System.Text.RegularExpressions;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// ConversacionesConfiguracion.razor es una página Blazor con ~20 servicios inyectados y sin arnés de
/// componentes (no hay bUnit en el repo); instrumentar uno sólo para estos dos fixes puntuales sería
/// desproporcionado. Sigue el mismo patrón ya establecido en WhatsAppEmbeddedSignupJavaScriptTests.cs /
/// EmbeddedSignupMultiTenantTests.cs: verificar el CONTRATO por texto fuente. Cubre los dos bugs de UI
/// confirmados en la auditoría Base4264: (1) _embeddedSignupStatus congelado tras timeout/cancelación,
/// (2) _embeddedSignupBusy compartido causando dos spinners simultáneos.
/// </summary>
public sealed class ConversacionesConfiguracionEmbeddedSignupUiTests
{
    [Fact]
    public void CancellationPath_RefreshesEmbeddedSignupStatus_InsteadOfStayingStale()
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

        // Tanto el watchdog de 90s como la cancelación explícita del SDK pasan por este único método
        // (es la corrección mínima: un solo lugar arreglado, sin duplicar la lógica de refresh).
        Assert.Contains("await PersistEmbeddedSignupCancellationAsync();", source, StringComparison.Ordinal);
        Assert.Contains("return PersistEmbeddedSignupCancellationAsync();", source, StringComparison.Ordinal);
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
