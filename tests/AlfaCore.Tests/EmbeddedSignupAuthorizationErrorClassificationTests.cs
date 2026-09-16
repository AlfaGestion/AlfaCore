using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Fase 2 de la auditoría UX (bug confirmado en Base4264, IdOnboarding
/// d23ddd26-393b-4334-a0b9-87b1bb6e2098): CompleteEmbeddedSignupAuthorization tenía un
/// "catch { }" ciego que descartaba la excepción real de HandleAuthorizationCallbackAsync y
/// siempre mostraba "No se pudo completar la autorización con Meta.", sin importar la causa real
/// (la carrera watchdog/callback incluida). ClassifyEmbeddedSignupAuthorizationError reemplaza ese
/// catch ciego: clasifica la excepción real en un AppUiMessage accionable, sin exponer nunca
/// token/code/state/stack trace -- sólo texto estático conocido más el incidente sanitizado ya
/// generado por AppEvents.LogErrorAsync.
/// </summary>
public sealed class EmbeddedSignupAuthorizationErrorClassificationTests
{
    [Fact]
    public void ExpiredOrReusedState_ClassifiesAsActionRequired_NeverAsGenericError()
    {
        var ex = new UnauthorizedAccessException("La autorización venció o ya fue utilizada.");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-1");

        Assert.Equal(AppUiFeedbackSeverity.ActionRequired, message.Severity);
        Assert.DoesNotContain("venció o ya fue utilizada", message.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nuevamente", message.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionMismatch_ClassifiesAsWarning_DistinctFromExpired()
    {
        var ex = new UnauthorizedAccessException("La sesión de autorización no pertenece a esta base o usuario.");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-2");

        Assert.Equal(AppUiFeedbackSeverity.Warning, message.Severity);
    }

    [Fact]
    public void IncompleteMetaCallback_ClassifiesAsWarning()
    {
        var ex = new ArgumentException("La autorización de Meta está incompleta.");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-3");

        Assert.Equal(AppUiFeedbackSeverity.Warning, message.Severity);
    }

    [Fact]
    public void MetaRejectedOAuthExchange_ClassifiesAsWarning_NotAsInternalFailure()
    {
        var ex = new InvalidOperationException("Meta rechazó el intercambio OAuth (400).");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-4");

        Assert.Equal(AppUiFeedbackSeverity.Warning, message.Severity);
        Assert.False(message.HasCode);
    }

    [Fact]
    public void MissingAppSecretConfiguration_ClassifiesAsInternalError_WithIncidentCode()
    {
        var ex = new InvalidOperationException("Falta configurar el secreto privado de la Meta App.");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-5");

        Assert.Equal(AppUiFeedbackSeverity.Error, message.Severity);
        Assert.Equal("INC-5", message.Code);
    }

    [Fact]
    public void TransientHttpFailure_ClassifiesAsRetryableWarning()
    {
        var ex = new HttpRequestException("Connection reset");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-6");

        Assert.Equal(AppUiFeedbackSeverity.Warning, message.Severity);
    }

    [Fact]
    public void UnknownException_ClassifiesAsInternalError_AndNeverLeaksRawMessage()
    {
        var ex = new InvalidCastException("some internal CLR detail that must never reach the user");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-7");

        Assert.Equal(AppUiFeedbackSeverity.Error, message.Severity);
        Assert.DoesNotContain("CLR", message.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CLR", message.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("INC-7", message.Code);
    }

    [Theory]
    [InlineData("some-oauth-code-123")]
    [InlineData("state-hash-abc")]
    [InlineData("EAAG-token-value")]
    public void Classification_NeverEchoesArbitraryExceptionData(string secretLikeValue)
    {
        // Ninguna clasificación conocida debe interpolar datos arbitrarios de la excepción (code/state/
        // token) en el mensaje -- todos los textos son estáticos. Este test documenta esa garantía:
        // si alguna rama futura empezara a interpolar ex.Message crudo, un secreto podría filtrarse.
        var ex = new InvalidOperationException($"Meta rechazó el intercambio OAuth: {secretLikeValue}");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(ex, "INC-8");

        Assert.DoesNotContain(secretLikeValue, message.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(secretLikeValue, message.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretLikeValue, message.Suggestion, StringComparison.Ordinal);
        Assert.DoesNotContain(secretLikeValue, message.Code, StringComparison.Ordinal);
    }
}
