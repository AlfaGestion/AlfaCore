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
    Ready,
    Cancelled,
    ActionRequired,
    FailedRetryable,
    FailedFinal,
    Expired
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

public sealed record WhatsAppEmbeddedStartRequest(int IdBase, string IdCliente, string UsuarioIniciador);
public sealed record WhatsAppEmbeddedStartResult(Guid IdOnboarding, string State, DateTime ExpiresAtUtc);
public sealed record WhatsAppEmbeddedAuthorizationCallback(Guid IdOnboarding, int IdBase, string State, string AuthorizationCode, string Usuario, string WabaId = "", string PhoneNumberId = "");
public sealed record WhatsAppEmbeddedRetryRequest(Guid IdOnboarding, string Usuario);

public sealed record WhatsAppEmbeddedProgressItem(string Key, string Label, WhatsAppEmbeddedProgressState State);
public enum WhatsAppEmbeddedProgressState { Pending, InProgress, Completed, ActionRequired, Failed }

public sealed class WhatsAppEmbeddedStatusView
{
    public Guid IdOnboarding { get; init; }
    public WhatsAppEmbeddedOnboardingStatus Status { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string IncidentId { get; init; } = string.Empty;
    public IReadOnlyList<WhatsAppEmbeddedProgressItem> Progress { get; init; } = [];
}

public enum WhatsAppAssetOwnershipResult { Reserved, AlreadyOwnedByBase, Conflict }
public sealed record WhatsAppAssetOwnershipDecision(WhatsAppAssetOwnershipResult Result, int OwnerBaseId, string AssetId);
public sealed record WhatsAppWabaOwnership(string WabaId, int IdBase, string MetaBusinessId, DateTime ModifiedAtUtc);
public sealed record WhatsAppPhoneOwnership(string PhoneNumberId, string WabaId, int IdBase, DateTime ModifiedAtUtc);

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
