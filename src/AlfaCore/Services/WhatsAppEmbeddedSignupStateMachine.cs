using AlfaCore.Models;

namespace AlfaCore.Services;

public static class WhatsAppEmbeddedSignupStateMachine
{
    private static readonly IReadOnlyDictionary<WhatsAppEmbeddedOnboardingStatus, HashSet<WhatsAppEmbeddedOnboardingStatus>> Allowed =
        new Dictionary<WhatsAppEmbeddedOnboardingStatus, HashSet<WhatsAppEmbeddedOnboardingStatus>>
        {
            [WhatsAppEmbeddedOnboardingStatus.Started] = [WhatsAppEmbeddedOnboardingStatus.Authorized, WhatsAppEmbeddedOnboardingStatus.Cancelled, WhatsAppEmbeddedOnboardingStatus.Expired, WhatsAppEmbeddedOnboardingStatus.FailedFinal],
            [WhatsAppEmbeddedOnboardingStatus.Authorized] = [WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable, WhatsAppEmbeddedOnboardingStatus.FailedFinal],
            [WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets] = [WhatsAppEmbeddedOnboardingStatus.ValidatingOwnership, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable, WhatsAppEmbeddedOnboardingStatus.FailedFinal],
            [WhatsAppEmbeddedOnboardingStatus.ValidatingOwnership] = [WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable, WhatsAppEmbeddedOnboardingStatus.FailedFinal],
            [WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess] = [WhatsAppEmbeddedOnboardingStatus.SubscribingWabas, WhatsAppEmbeddedOnboardingStatus.FailedRetryable, WhatsAppEmbeddedOnboardingStatus.FailedFinal],
            [WhatsAppEmbeddedOnboardingStatus.SubscribingWabas] = [WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment, WhatsAppEmbeddedOnboardingStatus.FailedRetryable, WhatsAppEmbeddedOnboardingStatus.FailedFinal],
            [WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment] = [WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable],
            [WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones] = [WhatsAppEmbeddedOnboardingStatus.RegisteringPhones, WhatsAppEmbeddedOnboardingStatus.Importing, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable],
            [WhatsAppEmbeddedOnboardingStatus.RegisteringPhones] = [WhatsAppEmbeddedOnboardingStatus.Importing, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable],
            [WhatsAppEmbeddedOnboardingStatus.Importing] = [WhatsAppEmbeddedOnboardingStatus.Ready, WhatsAppEmbeddedOnboardingStatus.ActionRequired, WhatsAppEmbeddedOnboardingStatus.FailedRetryable, WhatsAppEmbeddedOnboardingStatus.FailedFinal],
            [WhatsAppEmbeddedOnboardingStatus.FailedRetryable] = [WhatsAppEmbeddedOnboardingStatus.Authorized, WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets, WhatsAppEmbeddedOnboardingStatus.ValidatingOwnership, WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess, WhatsAppEmbeddedOnboardingStatus.SubscribingWabas, WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment, WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones, WhatsAppEmbeddedOnboardingStatus.RegisteringPhones, WhatsAppEmbeddedOnboardingStatus.Importing, WhatsAppEmbeddedOnboardingStatus.FailedFinal, WhatsAppEmbeddedOnboardingStatus.Expired],
            [WhatsAppEmbeddedOnboardingStatus.ActionRequired] = [WhatsAppEmbeddedOnboardingStatus.Authorized, WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment, WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones, WhatsAppEmbeddedOnboardingStatus.FailedFinal, WhatsAppEmbeddedOnboardingStatus.Expired]
        };

    public static bool CanTransition(WhatsAppEmbeddedOnboardingStatus from, WhatsAppEmbeddedOnboardingStatus to)
        => from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    public static void EnsureTransition(WhatsAppEmbeddedOnboardingStatus from, WhatsAppEmbeddedOnboardingStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"Transición de onboarding inválida: {from} → {to}.");
    }

    public static bool IsExpired(WhatsAppEmbeddedOnboardingDto onboarding, DateTime nowUtc)
        => onboarding.ExpiresAtUtc <= nowUtc && onboarding.Status is not WhatsAppEmbeddedOnboardingStatus.Ready;

    public static bool CanConsumeState(WhatsAppEmbeddedOnboardingDto onboarding, string expectedHash, string usuario, DateTime nowUtc)
        => onboarding.Status == WhatsAppEmbeddedOnboardingStatus.Started
           && onboarding.StateConsumedAtUtc is null
           && onboarding.ExpiresAtUtc > nowUtc
           && string.Equals(onboarding.StateHash, expectedHash, StringComparison.Ordinal)
           && string.Equals(onboarding.UsuarioIniciador, usuario, StringComparison.OrdinalIgnoreCase);

    public static DateTime ScheduleRetry(DateTime nowUtc, int retryCount, int initialDelaySeconds, int maxDelaySeconds)
    {
        var exponent = Math.Clamp(retryCount, 0, 20);
        var seconds = Math.Min(maxDelaySeconds, initialDelaySeconds * Math.Pow(2, exponent));
        return nowUtc.AddSeconds(seconds);
    }
}

public static class WhatsAppAssetOwnershipPolicy
{
    public static WhatsAppAssetOwnershipResult Evaluate(int? existingOwnerBaseId, int requestedBaseId)
        => !existingOwnerBaseId.HasValue
            ? WhatsAppAssetOwnershipResult.Reserved
            : existingOwnerBaseId.Value == requestedBaseId
                ? WhatsAppAssetOwnershipResult.AlreadyOwnedByBase
                : WhatsAppAssetOwnershipResult.Conflict;
}
