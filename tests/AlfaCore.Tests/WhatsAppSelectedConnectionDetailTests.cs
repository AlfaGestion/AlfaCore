using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Caso real reportado durante validación manual: Base con "Ministerio Vida de Dios - prueba Meta"
/// (operativo) y "AlfaNet" (FAILED_RETRYABLE, "No pudimos completar la activación"). Al seleccionar
/// AlfaNet, el panel derecho seguía mostrando "WhatsApp conectado" -- el estado de OTRA conexión
/// mezclado con la seleccionada, porque el panel se pintaba desde un resumen GLOBAL del módulo
/// (EmbeddedSignupUiState), no desde la conexión puntual seleccionada.
///
/// BuildPendingConnectionFeedbackPanel es la fuente de verdad para el detalle de una conexión no
/// operativa -- puro, testeado acá directamente con dos conexiones distintas para confirmar que nunca
/// se mezclan sin importar el orden en que se construyan.
/// </summary>
public sealed class WhatsAppSelectedConnectionDetailTests
{
    private const int MaxManualRetryCount = 2;

    private static WhatsAppEmbeddedPendingConnection Build(
        string nombre,
        WhatsAppEmbeddedOnboardingStatus status,
        int retryCount = 0,
        string errorSummary = "",
        string currentStep = "",
        WhatsAppEmbeddedOnboardingMode mode = WhatsAppEmbeddedOnboardingMode.Standard)
        => new(Guid.NewGuid(), status, mode, nombre, "", "123", null, retryCount, errorSummary, "", currentStep, DateTime.UtcNow);

    [Fact]
    public void TwoConnections_SelectingTheFailedOne_ShowsFailed_NeverTheOthersConnectedState()
    {
        // "Ministerio Vida de Dios" (A) está READY -- pero A ni siquiera pasa por
        // BuildPendingConnectionFeedbackPanel (los números operativos usan la rama SelectedApiNumero,
        // que no comparte código con ésta -- ver EmbeddedSignupFeedback_CanNeverRenderInsideTheOperationalNumberDetail).
        // B ("AlfaNet") es la seleccionada acá.
        var alfaNet = Build("AlfaNet", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, retryCount: 0, errorSummary: "Meta rechazó el token.");

        var panel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(alfaNet, MaxManualRetryCount)!;

        Assert.Equal("No pudimos completar la conexión", panel.Title);
        Assert.DoesNotContain("conectado", panel.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ya está listo", panel.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoFailedConnections_NeverMixTheirDetail_RegardlessOfConstructionOrder()
    {
        var connectionA = Build("Ministerio Vida de Dios", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, retryCount: 0, errorSummary: "Error de A", currentStep: "SUBSCRIBING_WABAS");
        var connectionB = Build("AlfaNet", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, retryCount: 1, errorSummary: "Error de B", currentStep: "DISCOVERING_PHONES");

        var panelA = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(connectionA, MaxManualRetryCount)!;
        var panelB = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(connectionB, MaxManualRetryCount)!;

        Assert.Contains("Error de A", panelA.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Error de B", panelA.Message, StringComparison.Ordinal);
        Assert.Contains("Error de B", panelB.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Error de A", panelB.Message, StringComparison.Ordinal);

        // Intento 1 de 2 vs. intento 2 de 2 -- cada una con SU PROPIO RetryCount, nunca compartido.
        Assert.Contains("Intento 1 de 2", panelA.Message, StringComparison.Ordinal);
        Assert.Contains("Intento 2 de 2", panelB.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardAndCoexistence_DoNotMixTheirDetailEither()
    {
        var standard = Build("Standard WhatsApp", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, errorSummary: "Falla Standard", mode: WhatsAppEmbeddedOnboardingMode.Standard);
        var coexistence = Build("Coexistence WhatsApp", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, errorSummary: "Falla Coexistence", mode: WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence);

        var standardPanel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(standard, MaxManualRetryCount)!;
        var coexistencePanel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(coexistence, MaxManualRetryCount)!;

        Assert.Contains("Falla Standard", standardPanel.Message, StringComparison.Ordinal);
        Assert.Contains("Falla Coexistence", coexistencePanel.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "Reintentar")]
    [InlineData(1, "Reintentar")]
    [InlineData(2, "Volver a conectar con Meta")]
    [InlineData(3, "Volver a conectar con Meta")]
    public void RetryCount_DrivesWhichPrimaryActionIsOffered(int retryCount, string expectedAction)
    {
        var pending = Build("AlfaNet", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, retryCount);
        var panel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(pending, MaxManualRetryCount)!;

        Assert.Equal(expectedAction, panel.PrimaryActionLabel);
    }

    [Fact]
    public void ExhaustedRetries_NeverOffersBothRetryAndReconnectSimultaneously()
    {
        var pending = Build("AlfaNet", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, retryCount: 2);
        var panel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(pending, MaxManualRetryCount)!;

        Assert.Equal("Volver a conectar con Meta", panel.PrimaryActionLabel);
        Assert.NotEqual("Reintentar", panel.PrimaryActionLabel);
    }

    [Fact]
    public void ReadyStateNeverReachesThisPanel_OnlyPendingNonOperationalStatusesDo()
    {
        // READY nunca aparece en _embeddedPendingConnections (GetPendingForBaseAsync lo excluye
        // explícitamente -- ver WhatsAppEmbeddedSignupStore) -- un número READY se convierte en un
        // ConversacionWhatsAppNumeroDto real y usa la rama SelectedApiNumero, nunca ésta. Documentado
        // acá con un valor por-las-dudas: si alguna vez SÍ llegara Ready a este método (no debería), no
        // debe ofrecer "Reintentar".
        var pending = Build("Ready por error", WhatsAppEmbeddedOnboardingStatus.Ready);
        var panel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(pending, MaxManualRetryCount);

        Assert.True(panel is null || panel.PrimaryActionLabel != "Reintentar");
    }

    [Fact]
    public void StepMapping_NeverShowsTheRawTechnicalStepName()
    {
        var pending = Build("AlfaNet", WhatsAppEmbeddedOnboardingStatus.FailedRetryable, currentStep: "SUBSCRIBING_WABAS");
        var panel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(pending, MaxManualRetryCount)!;

        Assert.DoesNotContain("SUBSCRIBING_WABAS", panel.Message, StringComparison.Ordinal);
        Assert.Contains("Configurando permisos de WhatsApp", panel.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SUBSCRIBING_WABAS", "Configurando permisos de WhatsApp")]
    [InlineData("DISCOVERING_PHONES", "Verificando el número")]
    [InlineData("CHECKING_CUSTOMER_PAYMENT", "Verificando la configuración de la cuenta")]
    [InlineData("IMPORTING", "Terminando la configuración en AlfaCore")]
    public void StepMapping_MatchesTheExamplesFromTheRequest(string step, string expectedFriendlyText)
        => Assert.Equal(expectedFriendlyText, ConversacionesConfiguracion.MapOnboardingStepToFriendlyText(step));

    [Fact]
    public void FeedbackPanel_NeverIncludesSecretsOrInternalIdentifiers()
    {
        var pending = Build(
            "AlfaNet",
            WhatsAppEmbeddedOnboardingStatus.FailedRetryable,
            errorSummary: "Meta no pudo validar el token."); // ErrorSummary ya sanitizado por WhatsAppEmbeddedSignupErrorLogger -- no debería traer secretos en la práctica; se confirma igual que el panel no agrega nada técnico propio.
        var panel = ConversacionesConfiguracion.BuildPendingConnectionFeedbackPanel(pending, MaxManualRetryCount)!;
        var fullText = panel.Title + panel.Message + panel.Suggestion + panel.Code;

        Assert.DoesNotContain("OAuth", fullText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StateHash", fullText, StringComparison.Ordinal);
        Assert.DoesNotContain("WabaId", fullText, StringComparison.Ordinal);
        Assert.DoesNotContain("PhoneNumberId", fullText, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", fullText, StringComparison.Ordinal);
        Assert.DoesNotContain(pending.IdOnboarding.ToString(), fullText, StringComparison.Ordinal);
    }
}
