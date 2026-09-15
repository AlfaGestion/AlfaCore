using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Sección 9 de la auditoría: cada tarjeta de la lista "WhatsApp conectados" debe comunicar un único
/// estado coherente, con esta prioridad explícita: BLOCKED > ACTION_REQUIRED > FAILED > CONFIGURING >
/// READY_WITH_NO_USERS > READY. WhatsAppConnectionCardStatusExtensions es el único resolver -- ni la
/// fila de número operativo ni la de conexión pendiente calculan esto por su cuenta.
/// </summary>
public sealed class WhatsAppConnectionCardStatusTests
{
    [Fact]
    public void PriorityOrder_MatchesTheAuditedHierarchy()
    {
        Assert.True(WhatsAppConnectionCardStatus.Blocked > WhatsAppConnectionCardStatus.ActionRequired);
        Assert.True(WhatsAppConnectionCardStatus.ActionRequired > WhatsAppConnectionCardStatus.Failed);
        Assert.True(WhatsAppConnectionCardStatus.Failed > WhatsAppConnectionCardStatus.Configuring);
        Assert.True(WhatsAppConnectionCardStatus.Configuring > WhatsAppConnectionCardStatus.ReadyWithNoUsers);
        Assert.True(WhatsAppConnectionCardStatus.ReadyWithNoUsers > WhatsAppConnectionCardStatus.Ready);
    }

    [Fact]
    public void ForNumero_BlockedWinsOverNoUsers_EvenWhenBothConditionsApply()
    {
        var status = WhatsAppConnectionCardStatusExtensions.ForNumero(isBlocked: true, usuariosCount: 0);

        Assert.Equal(WhatsAppConnectionCardStatus.Blocked, status);
        Assert.Equal("Cuenta bloqueada", status.Label());
    }

    [Fact]
    public void ForNumero_WithUsersAndNotBlocked_IsReady()
    {
        var status = WhatsAppConnectionCardStatusExtensions.ForNumero(isBlocked: false, usuariosCount: 3);

        Assert.Equal(WhatsAppConnectionCardStatus.Ready, status);
        Assert.Equal("Conectado", status.Label());
    }

    [Fact]
    public void ForNumero_NoUsersAndNotBlocked_IsReadyWithNoUsers()
    {
        var status = WhatsAppConnectionCardStatusExtensions.ForNumero(isBlocked: false, usuariosCount: 0);

        Assert.Equal(WhatsAppConnectionCardStatus.ReadyWithNoUsers, status);
        Assert.Equal("Sin usuarios asignados", status.Label());
    }

    [Theory]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppConnectionCardStatus.ActionRequired)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.FailedRetryable, WhatsAppConnectionCardStatus.Failed)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.FailedFinal, WhatsAppConnectionCardStatus.Failed)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Expired, WhatsAppConnectionCardStatus.Failed)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Cancelled, WhatsAppConnectionCardStatus.Failed)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Authorized, WhatsAppConnectionCardStatus.Configuring)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Importing, WhatsAppConnectionCardStatus.Configuring)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.SyncingHistory, WhatsAppConnectionCardStatus.Configuring)]
    public void ForPending_MapsEveryOnboardingStatus_ToTheRightBucket(WhatsAppEmbeddedOnboardingStatus status, WhatsAppConnectionCardStatus expected)
        => Assert.Equal(expected, WhatsAppConnectionCardStatusExtensions.ForPending(status));

    [Fact]
    public void Ready_NeverAppearsAsAPendingConnection_BecauseGetPendingForBaseExcludesIt()
    {
        // Documentado también en WhatsAppSelectedConnectionDetailTests -- Ready sale de la lista de
        // pendientes y se convierte en un ConversacionWhatsAppNumeroDto real (ForNumero), nunca pasa
        // por ForPending en la práctica. Si alguna vez llegara, no debe reclamar prioridad alta.
        var status = WhatsAppConnectionCardStatusExtensions.ForPending(WhatsAppEmbeddedOnboardingStatus.Ready);

        Assert.Equal(WhatsAppConnectionCardStatus.Configuring, status);
        Assert.True(status < WhatsAppConnectionCardStatus.Failed);
    }

    [Fact]
    public void TwoDifferentNumeros_OneBlockedOneNot_NeverMixTheirStatus()
    {
        var blocked = WhatsAppConnectionCardStatusExtensions.ForNumero(isBlocked: true, usuariosCount: 2);
        var healthy = WhatsAppConnectionCardStatusExtensions.ForNumero(isBlocked: false, usuariosCount: 2);

        Assert.Equal(WhatsAppConnectionCardStatus.Blocked, blocked);
        Assert.Equal(WhatsAppConnectionCardStatus.Ready, healthy);
        Assert.NotEqual(blocked, healthy);
    }

    [Theory]
    [InlineData(WhatsAppConnectionCardStatus.Blocked, AlfaCore.Components.Shared.AlfaDesign.AlfaTagTone.Danger)]
    [InlineData(WhatsAppConnectionCardStatus.ActionRequired, AlfaCore.Components.Shared.AlfaDesign.AlfaTagTone.Warning)]
    [InlineData(WhatsAppConnectionCardStatus.Failed, AlfaCore.Components.Shared.AlfaDesign.AlfaTagTone.Warning)]
    [InlineData(WhatsAppConnectionCardStatus.Ready, AlfaCore.Components.Shared.AlfaDesign.AlfaTagTone.Success)]
    public void Tone_UsesADistinctVisualLanguagePerSeverity(WhatsAppConnectionCardStatus status, AlfaCore.Components.Shared.AlfaDesign.AlfaTagTone expected)
        => Assert.Equal(expected, status.Tone());
}
