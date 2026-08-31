using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class WhatsAppEmbeddedSignupOrchestrator(
    IWhatsAppEmbeddedSignupStore store,
    IWhatsAppEmbeddedSignupStateProtector stateProtector,
    IMetaOAuthClient metaOAuthClient,
    IWhatsAppCredentialVault credentialVault,
    IWhatsAppPhonePinVault phonePinVault,
    IMetaWhatsAppManagementClient managementClient,
    IWhatsAppAssetOwnershipStore ownershipStore,
    IWhatsAppEmbeddedSignupErrorLogger errorLogger,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IWhatsAppEmbeddedSignupOrchestrator
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public WhatsAppEmbeddedSignupOrchestrator(
        IWhatsAppEmbeddedSignupStore store,
        IWhatsAppEmbeddedSignupStateProtector stateProtector,
        IMetaOAuthClient metaOAuthClient,
        IWhatsAppCredentialVault credentialVault,
        IOptions<WhatsAppEmbeddedSignupOptions> options)
        : this(store, stateProtector, metaOAuthClient, credentialVault, new UnsupportedPhonePinVault(), new UnsupportedManagementClient(), new UnsupportedOwnershipStore(), new NullErrorLogger(), options)
    {
    }

    public async Task<WhatsAppEmbeddedStartResult> StartAsync(WhatsAppEmbeddedStartRequest request, CancellationToken ct = default)
    {
        EnsureBaseAllowed(request.IdBase);
        if (request.IdBase <= 0 || string.IsNullOrWhiteSpace(request.UsuarioIniciador))
            throw new ArgumentException("La base y el usuario iniciador son obligatorios.", nameof(request));

        var now = DateTime.UtcNow;
        var (state, hash) = stateProtector.Create();
        var item = new WhatsAppEmbeddedOnboardingDto
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = request.IdBase,
            IdCliente = request.IdCliente.Trim(),
            UsuarioIniciador = request.UsuarioIniciador.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
                : request.CorrelationId.Trim(),
            StateHash = hash,
            OnboardingMode = request.OnboardingMode,
            Status = WhatsAppEmbeddedOnboardingStatus.Started,
            CurrentStep = "STARTED",
            StartedAtUtc = now,
            ModifiedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.OnboardingExpirationMinutes)
        };
        await store.CreateAsync(item, ct);
        var persisted = await store.GetAsync(item.IdOnboarding, ct)
            ?? throw new InvalidOperationException("No se pudo verificar el onboarding recién creado.");
        if (persisted.OnboardingMode != request.OnboardingMode)
            throw new InvalidOperationException("El modo persistido del onboarding no coincide con el modo seleccionado.");
        return new(item.IdOnboarding, state, item.ExpiresAtUtc, persisted.OnboardingMode, persisted.CorrelationId);
    }

    public async Task HandleAuthorizationCallbackAsync(WhatsAppEmbeddedAuthorizationCallback callback, CancellationToken ct = default)
    {
        EnsureBaseAllowed(callback.IdBase);
        if (callback.IdOnboarding == Guid.Empty || callback.IdBase <= 0 || string.IsNullOrWhiteSpace(callback.Usuario))
            throw new UnauthorizedAccessException("La sesión de autorización no es válida.");
        if (string.IsNullOrWhiteSpace(callback.State) || string.IsNullOrWhiteSpace(callback.AuthorizationCode))
            throw new ArgumentException("La autorización de Meta está incompleta.", nameof(callback));

        var onboarding = await store.GetAsync(callback.IdOnboarding, ct)
            ?? throw new UnauthorizedAccessException("La sesión de autorización no existe.");
        if (onboarding.IdBase != callback.IdBase || !string.Equals(onboarding.UsuarioIniciador.Trim(), callback.Usuario.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("La sesión de autorización no pertenece a esta base o usuario.");
        if (!string.Equals(onboarding.StateHash, stateProtector.Hash(callback.State), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("El state de autorización no es válido.");

        var consumed = await store.ConsumeStateAsync(onboarding.StateHash, callback.IdBase, callback.Usuario.Trim(), DateTime.UtcNow, ct);
        if (consumed is null || consumed.IdOnboarding != callback.IdOnboarding)
            throw new UnauthorizedAccessException("La autorización venció o ya fue utilizada.");

        var context = new WhatsAppVaultSecretContext(
            callback.IdBase, callback.IdOnboarding, string.Empty, callback.WabaId.Trim(), callback.PhoneNumberId.Trim(),
            "META_EMBEDDED_SIGNUP_BUSINESS_AUTHORIZATION", null);
        var token = await metaOAuthClient.ExchangeCodeAsync(callback.AuthorizationCode, context, ct);
        try
        {
            await store.MarkAuthorizedAsync(callback.IdOnboarding, token.TokenReference.Value, string.Empty, ct);
        }
        catch
        {
            await credentialVault.RemoveAsync(token.TokenReference, ct);
            throw;
        }
    }

    public async Task HandleCancellationAsync(Guid idOnboarding, int idBase, string state, string usuario, CancellationToken ct = default)
    {
        EnsureBaseAllowed(idBase);
        var item = await store.GetAsync(idOnboarding, ct) ?? throw new UnauthorizedAccessException("La sesión de autorización no existe.");
        if (item.IdBase != idBase || !string.Equals(item.UsuarioIniciador.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase) || !string.Equals(item.StateHash, stateProtector.Hash(state), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("La sesión de autorización no pertenece a esta base o usuario.");
        var consumed = await store.ConsumeStateAsync(item.StateHash, idBase, usuario.Trim(), DateTime.UtcNow, ct);
        if (consumed is null) return;
        await store.UpdateStatusAsync(idOnboarding, WhatsAppEmbeddedOnboardingStatus.Started, WhatsAppEmbeddedOnboardingStatus.Cancelled, "CANCELLED", ct);
    }

    public async Task<WhatsAppEmbeddedStatusView?> GetStatusAsync(Guid idOnboarding, CancellationToken ct = default)
    {
        var item = await store.GetAsync(idOnboarding, ct);
        return item is null || !_options.IsAllowedForBase(item.IdBase) ? null : WhatsAppEmbeddedSignupProgressMapper.Map(item);
    }

    public async Task<WhatsAppEmbeddedStatusView?> GetLatestStatusForBaseAsync(int idBase, CancellationToken ct = default)
    {
        if (!_options.IsAllowedForBase(idBase)) return null;
        var item = await store.GetLatestForBaseAsync(idBase, ct);
        return item is null ? null : WhatsAppEmbeddedSignupProgressMapper.Map(item);
    }

    public async Task ProcessNextStepAsync(Guid idOnboarding, CancellationToken ct = default)
    {
        var item = await store.GetAsync(idOnboarding, ct)
            ?? throw new InvalidOperationException("El onboarding no existe.");
        EnsureBaseAllowed(item.IdBase);
        if (string.IsNullOrWhiteSpace(item.TokenReference))
            throw new InvalidOperationException("El onboarding no tiene una referencia de credencial autorizada.");

        var tokenReference = new WhatsAppCredentialReference(item.TokenReference.Trim());
        try
        {
            var inspection = await metaOAuthClient.InspectTokenAsync(tokenReference, ct);
            if (!inspection.IsValid)
            {
                await MarkReauthorizationRequiredAsync(item, ct);
                return;
            }

            switch (item.Status)
            {
                case WhatsAppEmbeddedOnboardingStatus.Authorized:
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets, "DISCOVERING_ASSETS", ct);
                    break;

                case WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets:
                    var discovered = await DiscoverAssetsAsync(item, tokenReference, ct);
                    if (discovered.Count == 0)
                    {
                        await MarkCustomerActionRequiredAsync(item, "Meta no devolvió números de WhatsApp autorizados.", ct);
                        return;
                    }
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.ValidatingOwnership, "VALIDATING_OWNERSHIP", ct);
                    break;

                case WhatsAppEmbeddedOnboardingStatus.ValidatingOwnership:
                    var ownedAssets = await DiscoverAssetsAsync(item, tokenReference, ct);
                    foreach (var waba in ownedAssets.GroupBy(x => new { x.BusinessId, x.WabaId }).Select(x => x.Key))
                    {
                        var decision = await ownershipStore.ReserveWabaAsync(waba.WabaId, item.IdBase, waba.BusinessId, ct);
                        if (decision.Result == WhatsAppAssetOwnershipResult.Conflict)
                        {
                            await MarkOwnershipConflictAsync(item, WhatsAppEmbeddedActionRequiredReason.WabaCrossTenantConflict, WhatsAppEmbeddedErrorCodes.WabaCrossTenantConflict, waba.WabaId, null, ct);
                            return;
                        }
                    }
                    foreach (var asset in ownedAssets)
                    {
                        var decision = await ownershipStore.ReservePhoneAsync(asset.PhoneNumberId, asset.WabaId, item.IdBase, ct);
                        if (decision.Result == WhatsAppAssetOwnershipResult.Conflict)
                        {
                            await MarkOwnershipConflictAsync(item, WhatsAppEmbeddedActionRequiredReason.PhoneCrossTenantConflict, WhatsAppEmbeddedErrorCodes.PhoneCrossTenantConflict, asset.WabaId, asset.PhoneNumberId, ct);
                            return;
                        }
                    }
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess, "CONFIGURING_ACCESS", ct);
                    break;

                case WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess:
                    foreach (var wabaId in (await DiscoverAssetsAsync(item, tokenReference, ct)).Select(x => x.WabaId).Distinct(StringComparer.Ordinal))
                        await managementClient.EnsureSystemUserAssignmentAsync(wabaId, tokenReference, ct);
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.SubscribingWabas, "SUBSCRIBING_WABAS", ct);
                    break;

                case WhatsAppEmbeddedOnboardingStatus.SubscribingWabas:
                    foreach (var wabaId in (await DiscoverAssetsAsync(item, tokenReference, ct)).Select(x => x.WabaId).Distinct(StringComparer.Ordinal))
                        await managementClient.EnsureWabaSubscriptionAsync(wabaId, item.IdBase, tokenReference, ct);
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment, "CHECKING_CUSTOMER_PAYMENT", ct);
                    break;

                case WhatsAppEmbeddedOnboardingStatus.CheckingCustomerPayment:
                    foreach (var wabaId in (await DiscoverAssetsAsync(item, tokenReference, ct)).Select(x => x.WabaId).Distinct(StringComparer.Ordinal))
                    {
                        if (await managementClient.GetCustomerPaymentReadinessAsync(wabaId, tokenReference, ct) == MetaCustomerPaymentReadiness.CustomerActionRequired)
                        {
                            await store.MarkActionRequiredAsync(item.IdOnboarding, WhatsAppEmbeddedActionRequiredReason.CustomerPaymentSetupRequired,
                                "El cliente debe completar el método de pago directamente en Meta.", string.Empty, ct);
                            return;
                        }
                    }
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones, "DISCOVERING_PHONES", ct);
                    break;

                case WhatsAppEmbeddedOnboardingStatus.DiscoveringPhones:
                    var readyAssets = await DiscoverAssetsAsync(item, tokenReference, ct);
                    var requiresRegistration = readyAssets.Any(x => x.RegistrationStatus == MetaPhoneRegistrationStatus.RegistrationRequired);
                    if (requiresRegistration)
                    {
                        if (item.OnboardingMode == WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence)
                        {
                            await MarkCustomerActionRequiredAsync(item, "WhatsApp Business App no puede pasar por el registro Cloud API.", ct);
                            return;
                        }
                        await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.RegisteringPhones, "REGISTRATION_REQUIRED", ct);
                        return;
                    }
                    if (readyAssets.Any(x => x.RegistrationStatus is MetaPhoneRegistrationStatus.Unknown or MetaPhoneRegistrationStatus.Pending))
                    {
                        await MarkCustomerActionRequiredAsync(item, "Meta todavía no informa el número como utilizable.", ct);
                        return;
                    }
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.Importing, "READY_FOR_IMPORT_APPROVAL", ct);
                    break;

                case WhatsAppEmbeddedOnboardingStatus.RegisteringPhones:
                    if (item.OnboardingMode != WhatsAppEmbeddedOnboardingMode.Standard)
                        throw new InvalidOperationException("Coexistence no admite registro Cloud API.");
                    var registrationAssets = await DiscoverAssetsAsync(item, tokenReference, ct);
                    foreach (var asset in registrationAssets)
                    {
                        if (asset.RegistrationStatus == MetaPhoneRegistrationStatus.Registered)
                            continue;
                        if (asset.RegistrationStatus != MetaPhoneRegistrationStatus.RegistrationRequired)
                        {
                            await MarkCustomerActionRequiredAsync(item, "Meta no informa un estado registrable para el número.", ct);
                            return;
                        }
                        var pinReference = await phonePinVault.GetOrCreateAsync(new WhatsAppVaultSecretContext(
                            item.IdBase, item.IdOnboarding, asset.BusinessId, asset.WabaId, asset.PhoneNumberId,
                            "CLOUD_API_REGISTRATION_PIN", null), ct);
                        await managementClient.RegisterPhoneAsync(asset.PhoneNumberId, pinReference, tokenReference, ct);
                        if (await managementClient.GetPhoneRegistrationStatusAsync(asset.PhoneNumberId, tokenReference, ct) != MetaPhoneRegistrationStatus.Registered)
                            throw new MetaWhatsAppManagementException("META_PHONE_NOT_REGISTERED", true, false, "Meta todavía no confirmó el registro del número.");
                    }
                    await store.UpdateStatusAsync(item.IdOnboarding, item.Status, WhatsAppEmbeddedOnboardingStatus.Importing, "READY_FOR_OPERATIONAL_UPSERT", ct);
                    break;

                default:
                    throw new InvalidOperationException($"El estado {item.Status} no admite avance manual en esta etapa.");
            }
        }
        catch (MetaWhatsAppManagementException ex)
        {
            var incident = await errorLogger.LogAsync(item.IdOnboarding, item.IdBase, item.CurrentStep, ex.ErrorCode, null, null, item.RetryCount, ct);
            if (ex.RequiresReauthorization)
                await store.MarkActionRequiredAsync(item.IdOnboarding, WhatsAppEmbeddedActionRequiredReason.ReauthorizationRequired, "La autorización de Meta debe renovarse.", incident, ct);
            else if (ex.IsTransient && item.RetryCount < _options.MaxRetryCount)
                await store.MarkRetryableFailureAsync(item.IdOnboarding, ex.ErrorCode, "Meta no pudo completar temporalmente la configuración.", incident,
                    WhatsAppEmbeddedSignupStateMachine.ScheduleRetry(DateTime.UtcNow, item.RetryCount, _options.RetryInitialDelaySeconds, _options.RetryMaxDelaySeconds), ct);
            else
                await store.MarkFinalFailureAsync(item.IdOnboarding, ex.ErrorCode, "No se pudo completar la configuración con Meta.", incident, ct);
        }
    }

    public Task RetryAsync(WhatsAppEmbeddedRetryRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Los reintentos del pipeline Meta pertenecen a ES-2.");

    private void EnsureBaseAllowed(int idBase)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("Embedded Signup todavía no está habilitado.");
        if (!_options.IsAllowedForBase(idBase))
            throw new UnauthorizedAccessException("Embedded Signup no está habilitado para esta base.");
    }

    private async Task<IReadOnlyList<AuthorizedWhatsAppAsset>> DiscoverAssetsAsync(WhatsAppEmbeddedOnboardingDto item, WhatsAppCredentialReference tokenReference, CancellationToken ct)
    {
        var result = new List<AuthorizedWhatsAppAsset>();
        IReadOnlyList<MetaAuthorizedBusiness> businesses;
        try
        {
            businesses = await managementClient.DiscoverAuthorizedBusinessesAsync(tokenReference, ct);
        }
        catch (MetaWhatsAppManagementException ex) when (ex.ErrorCode is "100" or "2500")
        {
            businesses = [];
        }
        foreach (var business in businesses)
        {
            var wabas = await managementClient.DiscoverWabasAsync(business.BusinessId, tokenReference, ct);
            foreach (var waba in wabas)
            {
                var phones = await managementClient.DiscoverPhoneNumbersAsync(waba.WabaId, tokenReference, ct);
                result.AddRange(phones.Select(phone => new AuthorizedWhatsAppAsset(
                    business.BusinessId, waba.WabaId, phone.PhoneNumberId, phone.DisplayPhoneNumber, phone.VerifiedName,
                    phone.RegistrationStatus, phone.QualityRating, item.OnboardingMode)));
            }
        }
        if (result.Count == 0 && await credentialVault.GetContextAsync(tokenReference, ct) is { } hint && !string.IsNullOrWhiteSpace(hint.WabaId))
        {
            var phones = await managementClient.DiscoverPhoneNumbersAsync(hint.WabaId, tokenReference, ct);
            result.AddRange(phones.Select(phone => new AuthorizedWhatsAppAsset(
                hint.MetaBusinessId, hint.WabaId, phone.PhoneNumberId, phone.DisplayPhoneNumber, phone.VerifiedName,
                phone.RegistrationStatus, phone.QualityRating, item.OnboardingMode)));
        }
        return result.GroupBy(x => x.PhoneNumberId, StringComparer.Ordinal).Select(x => x.First()).ToArray();
    }

    private async Task MarkReauthorizationRequiredAsync(WhatsAppEmbeddedOnboardingDto item, CancellationToken ct)
        => await store.MarkActionRequiredAsync(item.IdOnboarding, WhatsAppEmbeddedActionRequiredReason.ReauthorizationRequired, "La autorización de Meta venció o fue revocada.", string.Empty, ct);

    private async Task MarkCustomerActionRequiredAsync(WhatsAppEmbeddedOnboardingDto item, string summary, CancellationToken ct)
        => await store.MarkActionRequiredAsync(item.IdOnboarding, WhatsAppEmbeddedActionRequiredReason.CustomerActionRequired, summary, string.Empty, ct);

    private async Task MarkOwnershipConflictAsync(WhatsAppEmbeddedOnboardingDto item, WhatsAppEmbeddedActionRequiredReason reason, string errorCode, string? wabaId, string? phoneNumberId, CancellationToken ct)
    {
        var incident = await errorLogger.LogAsync(item.IdOnboarding, item.IdBase, "VALIDATING_OWNERSHIP", errorCode, wabaId, phoneNumberId, item.RetryCount, ct);
        await store.MarkActionRequiredAsync(item.IdOnboarding, reason, "El recurso de WhatsApp ya pertenece a otra base.", incident, ct);
    }

    private sealed class UnsupportedManagementClient : IMetaWhatsAppManagementClient
    {
        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
        public Task<IReadOnlyList<MetaAuthorizedBusiness>> DiscoverAuthorizedBusinessesAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<IReadOnlyList<MetaAuthorizedBusiness>>();
        public Task<IReadOnlyList<MetaWabaAsset>> DiscoverWabasAsync(string businessId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<IReadOnlyList<MetaWabaAsset>>();
        public Task EnsureSystemUserAssignmentAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<object>();
        public Task EnsureWabaSubscriptionAsync(string wabaId, int idBase, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<object>();
        public Task<IReadOnlyList<MetaPhoneAsset>> DiscoverPhoneNumbersAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<IReadOnlyList<MetaPhoneAsset>>();
        public Task<IReadOnlyList<MetaMessageTemplate>> DiscoverTemplatesAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<IReadOnlyList<MetaMessageTemplate>>();
        public Task<MetaPhoneRegistrationStatus> GetPhoneRegistrationStatusAsync(string phoneNumberId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<MetaPhoneRegistrationStatus>();
        public Task RegisterPhoneAsync(string phoneNumberId, WhatsAppPhonePinReference pinReference, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<object>();
        public Task<MetaCustomerPaymentReadiness> GetCustomerPaymentReadinessAsync(string wabaId, WhatsAppCredentialReference tokenReference, CancellationToken ct = default) => Unsupported<MetaCustomerPaymentReadiness>();
    }

    private sealed class UnsupportedPhonePinVault : IWhatsAppPhonePinVault
    {
        public Task<WhatsAppPhonePinReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> pin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppPhonePinReference reference, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => Unsupported<WhatsAppAssetOwnershipDecision>();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => Unsupported<WhatsAppAssetOwnershipDecision>();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => Unsupported<WhatsAppWabaOwnership?>();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Unsupported<WhatsAppPhoneOwnership?>();
    }

    private sealed class NullErrorLogger : IWhatsAppEmbeddedSignupErrorLogger
    {
        public Task<string> LogAsync(Guid idOnboarding, int idBase, string step, string errorCode, string? wabaId, string? phoneNumberId, int retryCount, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}

public static class WhatsAppEmbeddedSignupProgressMapper
{
    private static readonly (string Key, string Label, WhatsAppEmbeddedOnboardingStatus Status)[] Steps =
    [
        ("authorized", "Cuenta autorizada", WhatsAppEmbeddedOnboardingStatus.Authorized),
        ("assets", "Cuenta de WhatsApp encontrada", WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets),
        ("access", "Configurando permisos", WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess),
        ("webhooks", "Configurando webhooks", WhatsAppEmbeddedOnboardingStatus.SubscribingWabas),
        ("numbers", "Importando números", WhatsAppEmbeddedOnboardingStatus.Importing),
        ("history", "Recuperando conversaciones", WhatsAppEmbeddedOnboardingStatus.SyncingHistory),
        ("contacts", "Sincronizando contactos", WhatsAppEmbeddedOnboardingStatus.SyncingContacts)
    ];

    public static WhatsAppEmbeddedStatusView Map(WhatsAppEmbeddedOnboardingDto item)
    {
        var current = Array.FindIndex(Steps, x => x.Status == item.Status);
        var terminalReady = item.Status == WhatsAppEmbeddedOnboardingStatus.Ready;
        return new WhatsAppEmbeddedStatusView
        {
            IdOnboarding = item.IdOnboarding,
            Status = item.Status,
            OnboardingMode = item.OnboardingMode,
            ActionRequiredReason = item.ActionRequiredReason,
            Title = terminalReady ? "Listo" : "Conectando WhatsApp",
            Message = item.Status switch
            {
                WhatsAppEmbeddedOnboardingStatus.ActionRequired when item.ActionRequiredReason == WhatsAppEmbeddedActionRequiredReason.CustomerPaymentSetupRequired => "Para terminar de activar WhatsApp, agregá un método de pago en tu cuenta de Meta. Los cargos de WhatsApp se pagan directamente a Meta.",
                WhatsAppEmbeddedOnboardingStatus.ActionRequired => "Necesitamos que completes un paso en tu cuenta Meta para continuar.",
                WhatsAppEmbeddedOnboardingStatus.FailedRetryable => "No pudimos completar la configuración. Podrás reintentar.",
                WhatsAppEmbeddedOnboardingStatus.FailedFinal => "No se pudo completar la configuración.",
                WhatsAppEmbeddedOnboardingStatus.Authorized => "Autorización recibida correctamente. La configuración automática continuará en la próxima etapa.",
                WhatsAppEmbeddedOnboardingStatus.Cancelled => "Conexión cancelada. Podés intentarlo nuevamente.",
                _ => string.Empty
            },
            IncidentId = item.IncidentId,
            Progress = Steps.Select((step, index) => new WhatsAppEmbeddedProgressItem(
                step.Key,
                step.Label,
                terminalReady || (current >= 0 && index < current) ? WhatsAppEmbeddedProgressState.Completed
                    : index == current ? WhatsAppEmbeddedProgressState.InProgress
                    : WhatsAppEmbeddedProgressState.Pending)).ToArray()
        };
    }
}
