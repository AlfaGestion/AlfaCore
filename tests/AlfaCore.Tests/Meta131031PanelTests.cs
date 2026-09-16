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
    public void GetLastOutboundDeliveryErrorAsync_ScopesStrictlyByThisNumero_NeverLeaksAcrossNumbers()
    {
        // "error viejo de otro PhoneNumberId no afecta" / "otro número de la misma Base no se mezcla":
        // el JOIN filtra por c.IdNumeroWhatsApp = @IdNumero -- IdNumero es el id interno de AlfaCore,
        // único por número incluso dentro de la misma Base, así que un 131031 de otro número (misma
        // Base o no) nunca puede aparecer acá. No hay forma de correrlo contra SQL real en este entorno
        // (CONV_MENSAJES/CONV_CONVERSACIONES viven en la base TENANT, no en ALFA_CENTRAL -- el único
        // SqlIntegrationFact disponible apunta a una ALFA_CENTRAL de test) -- se confirma por la forma
        // exacta de la consulta en vez de con datos reales.
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<string?> GetLastOutboundDeliveryErrorAsync(", StringComparison.Ordinal);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion", methodBody, StringComparison.Ordinal);
        Assert.Contains("WHERE c.IdNumeroWhatsApp = @IdNumero AND m.Direction = N'SALIENTE'", methodBody, StringComparison.Ordinal);
        Assert.Contains("cmd.Parameters.AddWithValue(\"@IdNumero\", idNumero)", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueNumeroBlockedFeedbackRefresh_AlwaysRequeries_NeverSkipsBecauseOfAStaleCacheHit()
    {
        // BUG REAL encontrado en auditoría de staleness: esto antes se saltaba la consulta por completo
        // si el número ya estaba en _numeroBlockedFeedbackCache -- una vez detectado un 131031, el
        // panel quedaba pegado para siempre en esa sesión de browser aunque Meta reactivara la cuenta y
        // un envío posterior tuviera éxito. El fix elimina el guard de "ya está en cache" -- refresca
        // siempre que se lo llama.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));
        var methodStart = source.IndexOf("private void QueueNumeroBlockedFeedbackRefresh()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.DoesNotContain("_numeroBlockedFeedbackCache.ContainsKey", methodBody, StringComparison.Ordinal);
        Assert.Contains("RefreshNumeroBlockedFeedbackAsync(idNumero)", methodBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private async Task SendReplyAsync()")]
    [InlineData("private async Task SendTemplateAsync()")]
    [InlineData("private async Task SendOrRemoveReactionAsync(long messageId, string emoji, bool remove)")]
    public void EverySendPath_RefreshesTheBlockedPanel_AfterCompleting_SoItClearsWithoutSwitchingConversation(string methodSignature)
    {
        // Con el fix anterior (siempre requerir), alcanza con cambiar de conversación y volver para ver
        // el panel actualizado -- pero si el mensaje B exitoso se manda DESDE la misma conversación que
        // ya tenía el panel abierto, sin esto el panel seguiría "bloqueado" hasta que el usuario
        // navegara a otro lado. Cada camino de envío debe refrescar en su finally, éxito o error.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));
        var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"No se encontró {methodSignature}.");
        var methodBody = ExtractMethodBody(source, methodStart);

        var finallyIndex = methodBody.LastIndexOf("finally", StringComparison.Ordinal);
        Assert.True(finallyIndex >= 0, $"No se encontró bloque finally en {methodSignature}.");
        var finallyBody = methodBody[finallyIndex..];
        Assert.Contains("QueueNumeroBlockedFeedbackRefresh();", finallyBody, StringComparison.Ordinal);
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

    [Fact]
    public void GetBlockedNumeroIdsAsync_IsOneBatchedQuery_UsingTheSameIsAccountLockedClassifier()
    {
        // Sección 9 de la auditoría (jerarquía de tarjetas en la lista): "Cuenta bloqueada" debe poder
        // mostrarse en la LISTA, no sólo en el detalle -- pero consultar 131031 número por número en
        // cada refresh (incluido el polling cada 5s mientras hay un onboarding en curso) sería un N+1.
        // Esta consulta trae todos los números de una sola vez con una window function.
        var source = ReadServiceSource();
        var methodStart = source.IndexOf("public Task<HashSet<int>> GetBlockedNumeroIdsAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró GetBlockedNumeroIdsAsync.");
        var methodBody = ExtractMethodBody(source, methodStart);

        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY c.IdNumeroWhatsApp ORDER BY m.FechaHora DESC)", methodBody, StringComparison.Ordinal);
        Assert.Contains("WhatsAppOutboundErrorClassifier.IsAccountLocked(GetString(rd, 1))", methodBody, StringComparison.Ordinal);

        // Reusa el mismo criterio de "último envío falló" que GetLastOutboundDeliveryErrorAsync
        // (EstadoEnvio = ERROR_ENVIO), no un criterio distinto inventado para la lista.
        Assert.Contains("EstadoEnvio = N'ERROR_ENVIO'", methodBody, StringComparison.Ordinal);

        // Corte temprano con lista vacía: nunca debe emitir un IN () vacío ni pegarle a la base sin
        // números que preguntar.
        Assert.Contains("if (ids.Length == 0)", methodBody, StringComparison.Ordinal);
        Assert.Contains("return new HashSet<int>();", methodBody, StringComparison.Ordinal);
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
