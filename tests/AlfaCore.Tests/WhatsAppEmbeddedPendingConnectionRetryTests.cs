using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// CanRetry reusa el mismo RetryCount del dominio (WhatsAppEmbeddedOnboardingDto, vía
/// GetPendingConnectionsAsync) -- no un contador paralelo en la UI. El umbral (MaxManualRetryCount) lo
/// pasa quien llama, ya que es una política configurable (WhatsAppEmbeddedSignupOptions), no un valor
/// fijo en el modelo.
/// </summary>
public sealed class WhatsAppEmbeddedPendingConnectionRetryTests
{
    private static WhatsAppEmbeddedPendingConnection Build(WhatsAppEmbeddedOnboardingStatus status, int retryCount)
        => new(Guid.NewGuid(), status, WhatsAppEmbeddedOnboardingMode.Standard, "AlfaNet", "", "123", null, retryCount);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void CanRetry_RespectsMaxManualRetryCount(int retryCount, bool expected)
        => Assert.Equal(expected, Build(WhatsAppEmbeddedOnboardingStatus.FailedRetryable, retryCount).CanRetry(maxManualRetryCount: 2));

    [Theory]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Ready)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Cancelled)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.FailedFinal)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Expired)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.ActionRequired)]
    [InlineData(WhatsAppEmbeddedOnboardingStatus.Authorized)]
    public void CanRetry_IsAlwaysFalse_OutsideFailedRetryable_RegardlessOfRetryCount(WhatsAppEmbeddedOnboardingStatus status)
        => Assert.False(Build(status, retryCount: 0).CanRetry(maxManualRetryCount: 2));
}
