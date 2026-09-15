using AlfaCore.Models;
using AlfaCore.Components.Pages;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 9 (continuación): el usuario aclaró la regla de producto -- no hace falta un panel grande
/// permanente para un sync ya terminado bien, pero SÍ mientras está en curso o si algo requiere
/// atención (Declined/Expired/Failed). Completed sigue siendo la línea compacta del &lt;dl&gt;
/// (ahora con un check "✓" para que se note que terminó bien de un vistazo), y Pending no necesita
/// ningún panel todavía (no hay nada en curso que explicar).
/// </summary>
public sealed class WhatsAppCoexistenceSyncStatusUiTests
{
    private static WhatsAppCoexistenceSyncDto BuildSync(WhatsAppCoexistenceSyncStatus status)
        => new()
        {
            PhoneNumberId = "123",
            SyncType = WhatsAppCoexistenceSyncType.History,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow
        };

    [Theory]
    [InlineData(WhatsAppCoexistenceSyncStatus.Pending)]
    [InlineData(WhatsAppCoexistenceSyncStatus.Completed)]
    public void NoBigPanel_WhenNothingIsInProgressOrNeedsAttention(WhatsAppCoexistenceSyncStatus status)
    {
        var sync = BuildSync(status);

        Assert.Null(ConversacionesConfiguracion.BuildCoexistenceHistorySyncPanel(sync));
        Assert.Null(ConversacionesConfiguracion.BuildCoexistenceContactsSyncPanel(sync));
    }

    [Fact]
    public void InProgressPanel_UsesInProgressSeverity_ForBothRequestedAndInProgress()
    {
        foreach (var status in new[] { WhatsAppCoexistenceSyncStatus.Requested, WhatsAppCoexistenceSyncStatus.InProgress })
        {
            var sync = BuildSync(status);
            var history = ConversacionesConfiguracion.BuildCoexistenceHistorySyncPanel(sync);
            var contacts = ConversacionesConfiguracion.BuildCoexistenceContactsSyncPanel(sync);

            Assert.Equal(AppUiFeedbackSeverity.InProgress, history!.Severity);
            Assert.Equal(AppUiFeedbackSeverity.InProgress, contacts!.Severity);
        }
    }

    [Theory]
    [InlineData(WhatsAppCoexistenceSyncStatus.Declined, AppUiFeedbackSeverity.Warning)]
    [InlineData(WhatsAppCoexistenceSyncStatus.Expired, AppUiFeedbackSeverity.Warning)]
    [InlineData(WhatsAppCoexistenceSyncStatus.Failed, AppUiFeedbackSeverity.Error)]
    public void AttentionNeededStates_ShowAVisiblePanel_WithTheRightSeverity(WhatsAppCoexistenceSyncStatus status, AppUiFeedbackSeverity expectedSeverity)
    {
        var sync = BuildSync(status);

        var history = ConversacionesConfiguracion.BuildCoexistenceHistorySyncPanel(sync);
        var contacts = ConversacionesConfiguracion.BuildCoexistenceContactsSyncPanel(sync);

        Assert.Equal(expectedSeverity, history!.Severity);
        Assert.Equal(expectedSeverity, contacts!.Severity);
        // Nunca debe insinuar que el número dejó de funcionar -- la sincronización no bloquea READY.
        Assert.Contains("normalidad", history.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("normalidad", contacts.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullSync_NeverThrows_JustReturnsNoPanel()
    {
        Assert.Null(ConversacionesConfiguracion.BuildCoexistenceHistorySyncPanel(null));
        Assert.Null(ConversacionesConfiguracion.BuildCoexistenceContactsSyncPanel(null));
    }

    [Fact]
    public void ContactsPanel_UsesCorrectSpanishPluralAgreement_NotAGenericSharedTemplate()
    {
        var declined = BuildSync(WhatsAppCoexistenceSyncStatus.Declined);
        var contacts = ConversacionesConfiguracion.BuildCoexistenceContactsSyncPanel(declined)!;

        Assert.Equal("Contactos no compartidos", contacts.Title);
    }
}
