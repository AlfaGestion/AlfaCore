using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IWhatsAppEmbeddedSignupStore
{
    Task CreateAsync(WhatsAppEmbeddedOnboardingDto onboarding, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> GetAsync(Guid idOnboarding, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> GetLatestForBaseAsync(int idBase, CancellationToken ct = default);
    Task<IReadOnlyList<WhatsAppEmbeddedOnboardingDto>> GetPendingForBaseAsync(int idBase, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WhatsAppEmbeddedOnboardingDto>>([]);
    Task<WhatsAppEmbeddedOnboardingDto?> GetLatestReadyForBaseAsync(int idBase, CancellationToken ct = default)
        => Task.FromResult<WhatsAppEmbeddedOnboardingDto?>(null);
    Task<WhatsAppEmbeddedOnboardingDto?> ConsumeStateAsync(string stateHash, int idBase, string usuario, DateTime nowUtc, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus expectedStatus, WhatsAppEmbeddedOnboardingStatus nextStatus, string currentStep, CancellationToken ct = default);
    Task MarkAuthorizedAsync(Guid idOnboarding, string tokenReference, string metaBusinessId, CancellationToken ct = default);
    Task MarkActionRequiredAsync(Guid idOnboarding, WhatsAppEmbeddedActionRequiredReason reason, string summary, string incidentId, CancellationToken ct = default);
    Task MarkRetryableFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default);
    Task ScheduleRetryAsync(Guid idOnboarding, WhatsAppEmbeddedOnboardingStatus resumeStatus, string resumeStep, string errorCode, string summary, string incidentId, DateTime nextAttemptUtc, CancellationToken ct = default)
        => MarkRetryableFailureAsync(idOnboarding, errorCode, summary, incidentId, nextAttemptUtc, ct);
    /// <summary>
    /// <paramref name="failedStep"/> es el último paso real alcanzado (p. ej. "SUBSCRIBING_WABAS") para
    /// que la UI pueda mostrarlo. Si se omite, se conserva el comportamiento histórico (PasoActual="FAILED").
    /// </summary>
    Task MarkFinalFailureAsync(Guid idOnboarding, string errorCode, string summary, string incidentId, string? failedStep = null, CancellationToken ct = default);
    Task MarkReadyAsync(Guid idOnboarding, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextAsync(string workerId, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default);
    Task<WhatsAppEmbeddedOnboardingDto?> ClaimNextForBasesAsync(string workerId, IReadOnlyCollection<int> allowedBaseIds, DateTime nowUtc, DateTime claimExpiresAtUtc, CancellationToken ct = default)
        => ClaimNextAsync(workerId, nowUtc, claimExpiresAtUtc, ct);
    Task ReleaseClaimAsync(Guid idOnboarding, string workerId, DateTime? nextAttemptUtc, CancellationToken ct = default);

    /// <summary>
    /// Transición de dominio automática STARTED → EXPIRED, distinta de HandleCancellationAsync
    /// (no requiere state/usuario). Atómica: sólo afecta la fila si TODAS se cumplen:
    /// Estado == STARTED, StateConsumedAtUtc IS NULL, FechaExpiracionUtc &lt;= ahora, IdBase coincide.
    /// Devuelve true únicamente si esta llamada efectivamente realizó la transición.
    /// </summary>
    Task<bool> ExpireStaleStartedAsync(Guid idOnboarding, int idBase, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>
    /// Salida segura de UX para abandonar una configuración incompleta atascada en ACTION_REQUIRED
    /// (p. ej. la elección de modo de onboarding fue incorrecta y no hay forma de continuar). Distinta
    /// de HandleCancellationAsync (que exige state/usuario/StateHash de un STARTED activo). Atómica:
    /// sólo afecta la fila si IdOnboarding + IdBase + Estado=ACTION_REQUIRED coinciden exactamente
    /// (nunca cancela onboardings de otra Base). Nunca toca ownership/Vault/Meta/subscribed_apps/
    /// callback -- sólo cambia Estado/PasoActual de la fila, que queda preservada como historial.
    /// Devuelve true únicamente si esta llamada efectivamente realizó la transición.
    /// </summary>
    Task<bool> CancelActionRequiredAsync(Guid idOnboarding, int idBase, CancellationToken ct = default)
        => Task.FromResult(false);
}

public interface IWhatsAppAssetOwnershipStore
{
    Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default);
    Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default);
    Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default);
    Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default);

    /// <summary>
    /// ¿La base tiene "footprint" Embedded Signup en el central: ownership de WABA/phone o un
    /// onboarding no terminal? Read-only. Se usa para decidir fail-closed vs legacy passthrough
    /// cuando llega un webhook para un phone_number_id sin ownership.
    /// </summary>
    Task<bool> HasEmbeddedSignupFootprintAsync(int idBase, CancellationToken ct = default) => Task.FromResult(false);
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

    /// <summary>
    /// "Cancelar configuración": abandona explícitamente una configuración incompleta atascada en
    /// ACTION_REQUIRED para esa Base (p. ej. el modo de onboarding elegido fue incorrecto). Nunca
    /// toca ownership/Vault/Meta -- sólo transiciona el onboarding a CANCELLED, preservado como
    /// historial, para que la Base pueda iniciar una configuración nueva. Devuelve true únicamente si
    /// efectivamente canceló la fila esperada (IdOnboarding + IdBase + Estado=ACTION_REQUIRED).
    /// </summary>
    Task<bool> CancelActionRequiredConfigurationAsync(Guid idOnboarding, int idBase, CancellationToken ct = default)
        => Task.FromResult(false);
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

    /// <summary>
    /// POST /{phoneNumberId}/smb_app_data { messaging_product:"whatsapp", sync_type:"history"|"smb_app_state_sync" }.
    /// Sólo soporta esos dos sync_type. Nunca expone token/PIN/AppSecret -- únicamente captura request_id
    /// (best effort: si Meta no lo devuelve, RequestId queda vacío pero la llamada no se considera fallida).
    /// Sin implementación por defecto: los fakes de test que no la necesitan no compilan silenciosamente mal --
    /// deben optar explícitamente por NotSupportedException si no la usan.
    /// </summary>
    Task<MetaSmbAppDataSyncResult> RequestSmbAppDataSyncAsync(string phoneNumberId, WhatsAppCoexistenceSyncType syncType, WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
        => throw new NotSupportedException("Este cliente de administración de Meta no admite smb_app_data.");
}

public sealed record MetaTokenExchangeResult(WhatsAppCredentialReference TokenReference, DateTime? ExpiresAtUtc);
public sealed record MetaTokenInspectionResult(bool IsValid, DateTime? ExpiresAtUtc, IReadOnlyList<string> GrantedScopes);
public sealed record MetaAuthorizedBusiness(string BusinessId, string Name);
public sealed record MetaWabaAsset(string WabaId, string BusinessId, string Name);
public sealed record MetaPhoneAsset(string PhoneNumberId, string WabaId, string DisplayPhoneNumber, string VerifiedName, string Status, string QualityRating, MetaPhoneRegistrationStatus RegistrationStatus, bool IsOnBizApp = false);
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
    Exception? innerException = null,
    string? errorSubcode = null,
    int? httpStatusCode = null,
    TimeSpan? retryAfter = null,
    bool hasBusinessUseCaseUsage = false,
    TimeSpan? estimatedTimeToRegainAccess = null,
    string? errorType = null,
    string? metaErrorMessage = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
    public bool IsTransient { get; } = isTransient;
    public bool RequiresReauthorization { get; } = requiresReauthorization;
    public string? ErrorSubcode { get; } = errorSubcode;
    public int? HttpStatusCode { get; } = httpStatusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public bool HasBusinessUseCaseUsage { get; } = hasBusinessUseCaseUsage;
    public TimeSpan? EstimatedTimeToRegainAccess { get; } = estimatedTimeToRegainAccess;
    /// <summary>error.type de Graph (p. ej. "OAuthException"). Null si no vino un error HTTP real de Meta.</summary>
    public string? ErrorType { get; } = errorType;
    /// <summary>error.message de Graph, sanitizado (sin caracteres de control, truncado a 300). Nunca un token.</summary>
    public string? MetaErrorMessage { get; } = metaErrorMessage;
    public bool IsRateLimit => ErrorCode is "80008";
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

public interface IWhatsAppEmbeddedOperationalImportService
{
    Task<WhatsAppEmbeddedOperationalImportResult> CompleteForBaseAsync(Guid idOnboarding, int activeBaseId, CancellationToken ct = default);
}

/// <summary>
/// Tracking central y durable, por (IdBase, PhoneNumberId, SyncType), de las solicitudes one-shot de
/// sync inicial de Coexistence (history / smb_app_state_sync). La garantía "no repetir un one-shot ya
/// aceptado por Meta" vive en <see cref="TryReserveAsync"/>: sólo la llamada que efectivamente inserta
/// la fila puede disparar el POST a Meta -- cualquier reintento posterior (reinicio de IIS, worker,
/// replay de webhook, re-procesamiento) encuentra la fila ya existente y no hace nada.
/// </summary>
public interface IWhatsAppCoexistenceSyncStore
{
    /// <summary>
    /// Inserta atómicamente la fila si TODAVÍA NO existe para (IdBase, PhoneNumberId, SyncType).
    /// Devuelve true únicamente si esta llamada insertó la fila (es decir: es la única autorizada a
    /// proceder con el POST a Meta). <paramref name="initialStatus"/> es Pending (se va a pedir ahora)
    /// o Expired (la ventana de 24h ya venció -- se registra sin llamar nunca a Meta).
    /// </summary>
    Task<bool> TryReserveAsync(int idBase, string phoneNumberId, Guid idOnboarding, WhatsAppCoexistenceSyncType syncType,
        WhatsAppCoexistenceSyncStatus initialStatus, DateTime nowUtc, CancellationToken ct = default);

    Task MarkRequestedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, string requestId, DateTime requestedAtUtc, CancellationToken ct = default);
    Task MarkFailedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, string errorCode, string errorSummary, CancellationToken ct = default);

    /// <summary>Webhook: primer chunk de historial recibido. Guardado: sólo avanza desde Pending/Requested (nunca regresa desde un estado terminal).</summary>
    Task MarkInProgressAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, CancellationToken ct = default);

    /// <summary>Webhook: history_context.status=complete, o primera entrega de smb_app_state_sync. Guardado: sólo avanza desde un estado no terminal (Completed→Completed es no-op, nunca regresa).</summary>
    Task MarkCompletedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, DateTime completedAtUtc, CancellationToken ct = default);

    /// <summary>Webhook: error code=2593109 (el negocio rechazó compartir historial desde la app). Guardado: sólo desde un estado no terminal.</summary>
    Task MarkDeclinedAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, string errorCode, CancellationToken ct = default);

    Task<WhatsAppCoexistenceSyncDto?> GetAsync(int idBase, string phoneNumberId, WhatsAppCoexistenceSyncType syncType, CancellationToken ct = default);
    Task<IReadOnlyList<WhatsAppCoexistenceSyncDto>> GetForBaseAsync(int idBase, CancellationToken ct = default);
}

/// <summary>
/// Dispara los sync iniciales (contactos primero, historial después -- independientes entre sí) de un
/// onboarding Coexistence que acaba de llegar a READY. Nunca bloquea ni revierte READY: cualquier
/// fallo (POST a Meta, ventana vencida, persistencia) se registra en <see cref="IWhatsAppCoexistenceSyncStore"/>
/// y se swallow-ea -- el número queda operativo igual.
/// </summary>
public interface IWhatsAppCoexistenceSyncTrigger
{
    Task TriggerInitialSyncsAsync(
        WhatsAppEmbeddedOnboardingDto onboarding,
        IReadOnlyList<WhatsAppCoexistencePhoneCandidate> phones,
        WhatsAppCredentialReference tokenReference,
        CancellationToken ct = default);
}
