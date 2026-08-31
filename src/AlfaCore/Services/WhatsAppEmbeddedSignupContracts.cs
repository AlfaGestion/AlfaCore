using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IWhatsAppEmbeddedSignupStore
{
    Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> GetLatestReadyForBaseAsync(int idBase, CancellationToken ct = default)
        => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
    Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default);
    Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default);
    Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default);
    Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default);
    Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, CancellationToken ct = default);
    Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default);
    Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default);
}

public interface IWhatsAppAssetOwnershipStore
{
    Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default);
    Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default);
    Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default);
    Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default);
}

public interface IWhatsAppEmbeddedSignupStateProtector
{
    (string State, string Hash) Create();
    string Hash(string state);
}

public interface IWhatsAppEmbeddedSignupOrchestrator
{
    Task<WhatsAppEmbeddedStartResult> StartAsync(WhatsAppEmbeddedStartRequest request, CancellationToken ct = default);
    Task HandleAuthorizationCallbackAsync(WhatsAppEmbeddedAuthorizationCallback callback, CancellationToken ct = default);
    Task HandleCancellationAsync(Guid idOnboarding, int idBase, string state, string usuario, CancellationToken ct = default);
    Task<WhatsAppEmbeddedStatusView?> GetStatusAsync(Guid idOnboarding, CancellationToken ct = default);
    Task<WhatsAppEmbeddedStatusView?> GetLatestStatusForBaseAsync(int idBase, CancellationToken ct = default);
    Task ProcessNextStepAsync(Guid idOnboarding, CancellationToken ct = default);
    Task RetryAsync(WhatsAppEmbeddedRetryRequest request, CancellationToken ct = default);
}

public interface IMetaOAuthClient
{
    Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default);
    Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
}

public interface IMetaWhatsAppManagementClient
{
    Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pinReference, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
    Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default);
}

public sealed record MetaTokenExchangeResult(WhatsAppCredentialReference TokenReference, DateTime? ExpiresAtUtc);
public sealed record MetaTokenInspectionResult(bool IsValid, DateTime? ExpiresAtUtc, IReadOnlyList<string> GrantedScopes);
public sealed record MetaAuthorizedBusiness(string BusinessId, string Name);
public sealed record MetaWabaAsset(string WabaId, string BusinessId, string Name);
public sealed record MetaPhoneAsset(string PhoneNumberId, string WabaId, string DisplayPhoneNumber, string VerifiedName, string Status, string QualityRating, MetaPhoneRegistrationStatus RegistrationStatus);
public sealed record MetaMessageTemplate(string Id, string Name, string Language, string Status, string Category, string HeaderText, string BodyText, string FooterText);
public sealed record WhatsAppWabaRoutingConfiguration(string CallbackUrl, string VerifyToken);
public interface IWhatsAppWabaRoutingProvider
{
    Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default);
}
public enum MetaCustomerPaymentReadiness { Unknown, Ready, CustomerActionRequired }

public sealed class MetaWhatsAppManagementException(
    string errorCode,
    bool isTransient,
    bool requiresReauthorization,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
    public bool IsTransient { get; } = isTransient;
    public bool RequiresReauthorization { get; } = requiresReauthorization;
}

public interface IWhatsAppCredentialVault
{
    Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default);
    Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default);
    Task<WhatsAppVaultSecretContext?> GetContextAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => Task.FromResult<WhatsAppVaultSecretContext?>(null);
    Task<WhatsAppCredentialReference?> FindActiveCredentialAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default)
        => Task.FromResult<WhatsAppCredentialReference?>(null);
    Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default);
}

public interface IWhatsAppWebhookTenantGuard
{
    Task ValidateAsync(int currentBaseId, IEnumerable<string> phoneNumberIds, CancellationToken ct = default);
}

public interface IWhatsAppRuntimeCredentialResolver
{
    Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default);
}

public enum WhatsAppRuntimeCredentialOrigin { Legacy, EmbeddedSignup }
public sealed record WhatsAppRuntimeCredential(string WabaId, string PhoneNumberId, string GraphVersion, string AccessToken,
    WhatsAppRuntimeCredentialOrigin Origin, WhatsAppCredentialReference? CredentialReference = null);

public interface IWhatsAppPhonePinVault
{
    Task<WhatsAppPhonePinReference> GetOrCreateAsync(WhatsAppVaultSecretContext context, CancellationToken ct = default)
        => Task.FromException<WhatsAppPhonePinReference>(new NotSupportedException("El vault no implementa creación idempotente de PIN."));
    Task<WhatsAppPhonePinReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> pin, CancellationToken ct = default);
    Task<ReadOnlyMemory<char>> GetAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default);
    Task RemoveAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default);
}

public interface IWhatsAppEmbeddedSignupErrorLogger
{
    Task<string> LogAsync(Guid idOnboarding, int idBase, string step, string errorCode, string? wabaId, string? phoneNumberId, int retryCount, CancellationToken ct = default);
}
