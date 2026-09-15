using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 3 (continuación): lo implementado en la etapa anterior era un tooltip en el mensaje --
/// insuficiente para el caso real (Base84, error 131031 "Business Account locked"). Ahora hay un panel
/// grande y persistente en Configuración (detalle del número) y en Conversaciones (canal seleccionado),
/// ambos derivados del último mensaje saliente vía GetLastOutboundDeliveryErrorAsync -- nunca un flag
/// persistido, para no convertir un error puntual viejo en un "bloqueado" permanentemente falso.
/// </summary>
public sealed class Meta131031PanelTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void GetLastOutboundDeliveryErrorAsync_OnlyReturnsPayload_WhenLastOutboundIsAnError()
    {
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<string?> GetLastOutboundDeliveryErrorAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró GetLastOutboundDeliveryErrorAsync.");
        var methodBody = ExtractMethodBody(source, methodStart);

        // TOP (1) ... ORDER BY m.FechaHora DESC: siempre el intento MÁS RECIENTE, nunca un histórico
        // acumulado -- así un 131031 viejo se "autocorrige" en cuanto un envío posterior tiene éxito.
        Assert.Contains("SELECT TOP (1)", methodBody, StringComparison.Ordinal);
        Assert.Contains("ORDER BY m.FechaHora DESC", methodBody, StringComparison.Ordinal);
        Assert.Contains("string.Equals(estadoEnvio, \"ERROR_ENVIO\", StringComparison.OrdinalIgnoreCase)", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguracionNumeroDetail_ShowsBigPanel_ForLockedAccount_NotJustATooltip()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));

        Assert.Contains("private AppUiMessage? _selectedNumeroBlockedFeedback;", source, StringComparison.Ordinal);
        Assert.Contains("<AlfaFeedbackPanel Message=\"@_selectedNumeroBlockedFeedback\" Dismissible=\"false\" />", source, StringComparison.Ordinal);

        var methodStart = source.IndexOf("private async Task RefreshSelectedNumeroBlockedFeedbackAsync(int idNumero)", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);
        Assert.Contains("WhatsAppOutboundErrorClassifier.IsAccountLocked(payload)", methodBody, StringComparison.Ordinal);
        // No debe insinuar reconexión/retry como solución -- eso confundiría un bloqueo de cuenta con un
        // problema de credencial local.
        Assert.DoesNotContain("reconect", methodBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguracionBlockedPanel_NeverStalePastAnotherNumeroSelection()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));
        var methodStart = source.IndexOf("private async Task RefreshSelectedNumeroBlockedFeedbackAsync(int idNumero)", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        // Si el usuario ya cambió de número mientras la consulta estaba en vuelo, no debe pisar el
        // panel del número que ahora está seleccionado con el resultado (viejo) del anterior.
        Assert.Contains("if (_selectedApiNumeroId != idNumero)", methodBody, StringComparison.Ordinal);

        var selectMethodStart = source.IndexOf("private Task SelectApiNumero(ConversacionWhatsAppNumeroDto numero)", StringComparison.Ordinal);
        Assert.True(selectMethodStart >= 0);
        var selectMethodBody = ExtractMethodBody(source, selectMethodStart);
        Assert.Contains("_selectedNumeroBlockedFeedback = null;", selectMethodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversaciones_ShowsBigPanel_ForLockedChannel_WhenConversationSelected()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

        Assert.Contains("private readonly Dictionary<int, AppUiMessage?> _numeroBlockedFeedbackCache = [];", source, StringComparison.Ordinal);
        Assert.Contains("<AlfaFeedbackPanel Message=\"@SelectedConversationBlockedFeedback\" Dismissible=\"false\"", source, StringComparison.Ordinal);
        Assert.Contains("QueueNumeroBlockedFeedbackRefresh();", source, StringComparison.Ordinal);

        var methodStart = source.IndexOf("private async Task RefreshNumeroBlockedFeedbackAsync(int idNumero)", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);
        Assert.Contains("WhatsAppOutboundErrorClassifier.IsAccountLocked(payload)", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickReactionEmojisDoNotAccidentallyMatchBlockedPanelLogic()
    {
        // Test de cordura simple: IsAccountLocked no debe confundirse con "cualquier ERROR_ENVIO" --
        // sólo el código Graph 131031 exacto debe activar el panel de cuenta bloqueada.
        Assert.False(WhatsAppOutboundErrorClassifier.IsAccountLocked(null));
        Assert.False(WhatsAppOutboundErrorClassifier.IsAccountLocked(
            """{"Error":"Meta devolvió 400: {\"error\":{\"code\":131026,\"message\":\"Message undeliverable\"}}","Type":"System.Net.Http.HttpRequestException"}"""));
        Assert.True(WhatsAppOutboundErrorClassifier.IsAccountLocked(
            """{"Error":"Meta devolvió 400: {\"error\":{\"code\":131031,\"message\":\"Business Account locked\"}}","Type":"System.Net.Http.HttpRequestException"}"""));
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
