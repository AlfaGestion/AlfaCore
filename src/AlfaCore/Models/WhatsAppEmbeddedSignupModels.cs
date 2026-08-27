namespace AlfaCore.Models;

public enum WhatsAppEmbeddedOnboardingStatus
{
    Started,
    Authorized,
    DiscoveringAssets,
    ValidatingOwnership,
    ConfiguringAccess,
    SubscribingWabas,
    CheckingCustomerPayment,
    DiscoveringPhones,
    RegisteringPhones,
    Importing,
    SyncingHistory,
    SyncingContacts,
    Ready,
    Cancelled,
    ActionRequired,
    FailedRetryable,
    FailedFinal,
    Expired
}

public enum WhatsAppEmbeddedOnboardingMode
{
    Standard,
    BusinessAppCoexistence
}

public enum MetaPhoneRegistrationStatus { Unknown, RegistrationRequired, Pending, Registered }

public enum WhatsAppConnectionChoice
{
    ExistingWhatsAppBusiness,
    NewWhatsApp
}

public static class WhatsAppConnectionChoiceMapper
{
    public static WhatsAppEmbeddedOnboardingMode ToOnboardingMode(WhatsAppConnectionChoice choice)
        => choice switch
        {
            WhatsAppConnectionChoice.ExistingWhatsAppBusiness => WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence,
            WhatsAppConnectionChoice.NewWhatsApp => WhatsAppEmbeddedOnboardingMode.Standard,
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "La opción de conexión de WhatsApp no es válida.")
        };
}

public sealed class WhatsAppConnectionChoiceSelection
{
    public WhatsAppConnectionChoice? Selected { get; private set; }

    public void Begin() => Selected = null;

    public void Select(WhatsAppConnectionChoice choice)
    {
        if (!Enum.IsDefined(choice))
            throw new ArgumentOutOfRangeException(nameof(choice));
        Selected = choice;
    }

    public WhatsAppConnectionChoice Consume()
    {
        var selected = Selected
            ?? throw new InvalidOperationException("Elegí cómo querés usar WhatsApp antes de continuar.");
        Selected = null;
        return selected;
    }

    public void Clear() => Selected = null;
}

public enum WhatsAppEmbeddedActionRequiredReason
{
    CustomerPaymentSetupRequired,
    ReauthorizationRequired,
    CustomerActionRequired,
    WabaCrossTenantConflict,
    PhoneCrossTenantConflict
}

public static class WhatsAppEmbeddedErrorCodes
{
    public const string MetaAuthExpired = "META_AUTH_EXPIRED";
    public const string MetaRateLimit = "META_RATE_LIMIT";
    public const string MetaPermissionDenied = "META_PERMISSION_DENIED";
    public const string WabaCrossTenantConflict = "WABA_CROSS_TENANT_CONFLICT";
    public const string PhoneCrossTenantConflict = "PHONE_CROSS_TENANT_CONFLICT";
    public const string CustomerPaymentSetupRequired = "CUSTOMER_PAYMENT_SETUP_REQUIRED";
    public const string PhoneRegistrationRequired = "PHONE_REGISTRATION_REQUIRED";
    public const string UnknownMetaError = "UNKNOWN_META_ERROR";
}

public sealed class WhatsAppEmbeddedOnboardingDto
{
    public Guid IdOnboarding { get; set; }
    public int IdBase { get; set; }
    public string IdCliente { get; set; } = string.Empty;
    public string UsuarioIniciador { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string StateHash { get; set; } = string.Empty;
    public DateTime? StateConsumedAtUtc { get; set; }
    public WhatsAppEmbeddedOnboardingStatus Status { get; set; } = WhatsAppEmbeddedOnboardingStatus.Started;
    public WhatsAppEmbeddedOnboardingMode OnboardingMode { get; set; } = WhatsAppEmbeddedOnboardingMode.Standard;
    public string CurrentStep { get; set; } = string.Empty;
    public string MetaBusinessId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorSummary { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;
    public string TokenReference { get; set; } = string.Empty;
    public WhatsAppEmbeddedActionRequiredReason? ActionRequiredReason { get; set; }
    public string ClaimedBy { get; set; } = string.Empty;
    public DateTime? ClaimExpiresAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed record WhatsAppEmbeddedStartRequest(int IdBase, string IdCliente, string UsuarioIniciador, WhatsAppEmbeddedOnboardingMode OnboardingMode, string CorrelationId = "");
public sealed record WhatsAppEmbeddedStartResult(Guid IdOnboarding, string State, DateTime ExpiresAtUtc, WhatsAppEmbeddedOnboardingMode OnboardingMode, string CorrelationId);
public sealed record WhatsAppEmbeddedAuthorizationCallback(Guid IdOnboarding, int IdBase, string State, string AuthorizationCode, string Usuario, string WabaId = "", string PhoneNumberId = "");
public sealed record WhatsAppEmbeddedRetryRequest(Guid IdOnboarding, string Usuario);

public sealed record WhatsAppEmbeddedProgressItem(string Key, string Label, WhatsAppEmbeddedProgressState State);
public enum WhatsAppEmbeddedProgressState { Pending, InProgress, Completed, ActionRequired, Failed }

public sealed class WhatsAppEmbeddedStatusView
{
    public Guid IdOnboarding { get; init; }
    public WhatsAppEmbeddedOnboardingStatus Status { get; init; }
    public WhatsAppEmbeddedOnboardingMode OnboardingMode { get; init; }
    public WhatsAppEmbeddedActionRequiredReason? ActionRequiredReason { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string IncidentId { get; init; } = string.Empty;
    public IReadOnlyList<WhatsAppEmbeddedProgressItem> Progress { get; init; } = [];
}

public sealed record AuthorizedWhatsAppAsset(
    string BusinessId,
    string WabaId,
    string PhoneNumberId,
    string DisplayPhoneNumber,
    string VerifiedName,
    MetaPhoneRegistrationStatus RegistrationStatus,
    string QualityRating,
    WhatsAppEmbeddedOnboardingMode OnboardingMode);

public enum WhatsAppAssetOwnershipResult { Reserved, AlreadyOwnedByBase, Conflict }
public sealed record WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult Result, int OwnerBaseId, string AssetId);
public sealed record WhatsAppWabaOwnership(string WabaId, int IdBase, string MetaBusinessId, DateTime ModifiedAtUtc);
public sealed record WhatsAppPhoneOwnership(string PhoneNumberId, string WabaId, int IdBase, DateTime ModifiedAtUtc);
public sealed record WhatsAppConnectedNumberMetadata(string PhoneNumberId, WhatsAppEmbeddedOnboardingMode OnboardingMode)
{
    public string UserFacingConnection => OnboardingMode == WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence
        ? "WhatsApp Business + AlfaCore"
        : "AlfaCore";
}

public sealed record WhatsAppSynchronizationEvent(string EventName, string ExternalEventId, string PhoneNumberId);

public static class WhatsAppEmbeddedPipelinePolicy
{
    public static bool CanRegisterPhone(WhatsAppEmbeddedOnboardingMode mode)
        => mode == WhatsAppEmbeddedOnboardingMode.Standard;

    public static void EnsureCanRegisterPhone(WhatsAppEmbeddedOnboardingMode mode)
    {
        if (!CanRegisterPhone(mode))
            throw new InvalidOperationException("El modo WhatsApp Business + AlfaCore no admite registrar nuevamente el teléfono.");
    }

    public static string BuildHistoryIdempotencyKey(int idBase, string phoneNumberId, string externalEventId)
        => $"{idBase}:{phoneNumberId.Trim()}:{externalEventId.Trim()}";
}

// Referencias opacas: nunca contienen el secreto, el authorization code ni el PIN.
public sealed record WhatsAppCredentialReference(string Value);
public sealed record WhatsAppPhonePinReference(string Value);
public sealed record WhatsAppVaultSecretContext(
    int IdBase,
    Guid? IdOnboarding,
    string MetaBusinessId,
    string WabaId,
    string PhoneNumberId,
    string Purpose,
    DateTime? ExpiresAtUtc);
