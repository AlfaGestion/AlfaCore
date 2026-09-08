using AlfaCore.Models;
using AlfaCore.Configuration;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed record WhatsAppEmbeddedOperationalImportedNumber(
    int IdNumero,
    string Nombre,
    string DisplayPhoneNumber,
    string PhoneNumberId,
    string WabaId);

public sealed record WhatsAppEmbeddedOperationalImportResult(IReadOnlyList<WhatsAppEmbeddedOperationalImportedNumber> Numbers)
{
    public int IdNumero => Numbers.Count == 1 ? Numbers[0].IdNumero : 0;
    public string Nombre => Numbers.Count == 1 ? Numbers[0].Nombre : $"{Numbers.Count} números";
    public string DisplayPhoneNumber => Numbers.Count == 1 ? Numbers[0].DisplayPhoneNumber : string.Empty;
    public int Count => Numbers.Count;
}

public sealed record WhatsAppEmbeddedRecoveryCandidate(
    Guid IdOnboarding,
    string PhoneNumberId,
    string Nombre,
    string DisplayPhoneNumber,
    bool IsOperational);

public sealed class WhatsAppEmbeddedOperationalImportService(
    IWhatsAppEmbeddedSignupStore store,
    IWhatsAppCredentialVault credentialVault,
    IMetaWhatsAppManagementClient managementClient,
    IWhatsAppAssetOwnershipStore ownershipStore,
    IConversacionesConfigService conversacionesConfig,
    ISessionService sessionService,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IWhatsAppEmbeddedOperationalImportService
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;
    private readonly Dictionary<Guid, PendingConnectionMetadata> _pendingConnectionMetadata = [];

    public async Task<WhatsAppEmbeddedOperationalImportResult> CompleteAsync(Guid idOnboarding, CancellationToken ct = default)
    {
        var activeBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
        return await CompleteForBaseAsync(idOnboarding, activeBaseId, ct);
    }

    public async Task<WhatsAppEmbeddedOperationalImportResult> CompleteForBaseAsync(Guid idOnboarding, int activeBaseId, CancellationToken ct = default)
    {
        if (!_options.IsAllowedForBase(activeBaseId))
            throw new UnauthorizedAccessException("Embedded Signup no está habilitado para esta base.");
        var onboarding = await store.GetAsync(idOnboarding, ct)
            ?? throw new InvalidOperationException("El onboarding no existe.");
        if (activeBaseId <= 0 || onboarding.IdBase != activeBaseId)
            throw new UnauthorizedAccessException("El onboarding no pertenece a la base activa.");
        var readyForOperationalUpsert = onboarding.CurrentStep == "READY_FOR_OPERATIONAL_UPSERT"
            || (onboarding.OnboardingMode == WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence
                && onboarding.CurrentStep == "READY_FOR_IMPORT_APPROVAL");
        if (onboarding.Status != WhatsAppEmbeddedOnboardingStatus.Importing || !readyForOperationalUpsert)
            throw new InvalidOperationException("El onboarding no está listo para el alta operativa.");

        var tokenReference = new WhatsAppCredentialReference(onboarding.TokenReference.Trim());
        var context = await credentialVault.GetContextAsync(tokenReference, ct)
            ?? throw new InvalidOperationException("La credencial segura no tiene contexto vigente.");
        if (context.IdBase != activeBaseId || context.IdOnboarding != idOnboarding)
            throw new InvalidOperationException("El contexto seguro no coincide con el onboarding.");

        var phones = await DiscoverAuthorizedPhonesAsync(context, tokenReference, ct);
        if (phones.Count == 0)
            throw new InvalidOperationException("Meta no devolvió números de WhatsApp autorizados.");
        if (phones.Any(phone => phone.RegistrationStatus != MetaPhoneRegistrationStatus.Registered))
            throw new InvalidOperationException("Meta todavía no confirma todos los teléfonos como registrados.");

        foreach (var waba in phones.GroupBy(phone => new { phone.WabaId, phone.MetaBusinessId }).Select(group => group.Key))
        {
            var wabaOwnership = await ownershipStore.ReserveWabaAsync(waba.WabaId, activeBaseId, waba.MetaBusinessId, ct);
            if (wabaOwnership.Result == WhatsAppAssetOwnershipResult.Conflict)
                throw new UnauthorizedAccessException("El activo de WhatsApp pertenece a otra base.");
        }

        foreach (var phone in phones)
        {
            var phoneOwnership = await ownershipStore.ReservePhoneAsync(phone.PhoneNumberId, phone.WabaId, activeBaseId, ct);
            if (phoneOwnership.Result == WhatsAppAssetOwnershipResult.Conflict)
                throw new UnauthorizedAccessException("El activo de WhatsApp pertenece a otra base.");

            await conversacionesConfig.SaveWhatsAppNumeroAsync(new ConversacionWhatsAppNumeroDto
            {
                PhoneNumberId = phone.PhoneNumberId,
                Nombre = phone.VerifiedName,
                Activo = true,
                Usuarios = []
            }, ct);
        }

        var savedNumbers = (await conversacionesConfig.GetWhatsAppNumerosAsync(ct)).ToArray();
        var imported = phones
            .Select(phone =>
            {
                var saved = savedNumbers.Single(item => string.Equals(item.PhoneNumberId, phone.PhoneNumberId, StringComparison.Ordinal));
                return new WhatsAppEmbeddedOperationalImportedNumber(
                    saved.IdNumero,
                    saved.Nombre,
                    phone.DisplayPhoneNumber,
                    phone.PhoneNumberId,
                    phone.WabaId);
            })
            .ToArray();
        await store.MarkReadyAsync(idOnboarding, ct);
        return new WhatsAppEmbeddedOperationalImportResult(imported);
    }

    public async Task<IReadOnlyList<WhatsAppEmbeddedPendingConnection>> GetPendingConnectionsAsync(CancellationToken ct = default)
    {
        var activeBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
        if (!_options.IsAllowedForBase(activeBaseId))
            return [];

        var operationalPhoneIds = (await conversacionesConfig.GetWhatsAppNumerosAsync(ct))
            .Where(item => item.Activo && !string.IsNullOrWhiteSpace(item.PhoneNumberId))
            .Select(item => item.PhoneNumberId.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var pending = await store.GetPendingForBaseAsync(activeBaseId, ct);
        var result = new List<WhatsAppEmbeddedPendingConnection>();

        foreach (var onboarding in pending)
        {
            var name = "Nuevo WhatsApp";
            var displayPhoneNumber = string.Empty;
            var phoneNumberId = string.Empty;

            if (!string.IsNullOrWhiteSpace(onboarding.TokenReference))
            {
                var tokenReference = new WhatsAppCredentialReference(onboarding.TokenReference.Trim());
                var context = await credentialVault.GetContextAsync(tokenReference, ct);
                if (context is { IdBase: var contextBaseId, IdOnboarding: var contextOnboardingId }
                    && contextBaseId == activeBaseId
                    && contextOnboardingId == onboarding.IdOnboarding)
                {
                    phoneNumberId = context.PhoneNumberId.Trim();
                    if (_pendingConnectionMetadata.TryGetValue(onboarding.IdOnboarding, out var cached)
                        && string.Equals(cached.PhoneNumberId, phoneNumberId, StringComparison.Ordinal))
                    {
                        name = cached.Nombre;
                        displayPhoneNumber = cached.DisplayPhoneNumber;
                    }
                    else if (onboarding.Status != WhatsAppEmbeddedOnboardingStatus.Importing
                        && !string.IsNullOrWhiteSpace(context.WabaId) && !string.IsNullOrWhiteSpace(phoneNumberId))
                    {
                        try
                        {
                            var phone = (await managementClient.DiscoverPhoneNumbersAsync(context.WabaId, tokenReference, ct))
                                .SingleOrDefault(item => string.Equals(item.PhoneNumberId, phoneNumberId, StringComparison.Ordinal));
                            if (phone is not null)
                            {
                                name = string.IsNullOrWhiteSpace(phone.VerifiedName) ? name : phone.VerifiedName;
                                displayPhoneNumber = phone.DisplayPhoneNumber;
                                _pendingConnectionMetadata[onboarding.IdOnboarding] = new PendingConnectionMetadata(
                                    phoneNumberId,
                                    name,
                                    displayPhoneNumber);
                            }
                        }
                        catch (MetaWhatsAppManagementException)
                        {
                            // La tarjeta sigue siendo útil aunque Meta todavía no devuelva el detalle visible.
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(phoneNumberId) && operationalPhoneIds.Contains(phoneNumberId))
                continue;

            result.Add(new WhatsAppEmbeddedPendingConnection(
                onboarding.IdOnboarding,
                onboarding.Status,
                onboarding.OnboardingMode,
                name,
                displayPhoneNumber,
                phoneNumberId,
                onboarding.ActionRequiredReason));
        }

        return result;
    }

    public async Task<WhatsAppEmbeddedRecoveryCandidate?> GetRecoveryCandidateAsync(CancellationToken ct = default)
    {
        var activeBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
        if (!_options.IsAllowedForBase(activeBaseId))
            return null;

        var onboarding = await store.GetLatestReadyForBaseAsync(activeBaseId, ct);
        if (onboarding is null
            || onboarding.IdBase != activeBaseId
            || onboarding.Status != WhatsAppEmbeddedOnboardingStatus.Ready
            || onboarding.CurrentStep != "READY"
            || string.IsNullOrWhiteSpace(onboarding.TokenReference))
            return null;

        var tokenReference = new WhatsAppCredentialReference(onboarding.TokenReference.Trim());
        var context = await credentialVault.GetContextAsync(tokenReference, ct);
        if (context is null
            || context.IdBase != activeBaseId
            || context.IdOnboarding != onboarding.IdOnboarding
            || string.IsNullOrWhiteSpace(context.WabaId)
            || string.IsNullOrWhiteSpace(context.PhoneNumberId))
            return null;

        // Comprueba que el ciphertext siga siendo descifrable antes de ofrecer la recuperación.
        _ = await credentialVault.GetAsync(tokenReference, ct);

        var wabaOwnership = await ownershipStore.GetWabaOwnershipAsync(context.WabaId, ct);
        var phoneOwnership = await ownershipStore.GetPhoneOwnershipAsync(context.PhoneNumberId, ct);
        if (wabaOwnership?.IdBase != activeBaseId
            || phoneOwnership?.IdBase != activeBaseId
            || !string.Equals(phoneOwnership.WabaId, context.WabaId, StringComparison.Ordinal))
            return null;

        var phones = await managementClient.DiscoverPhoneNumbersAsync(context.WabaId, tokenReference, ct);
        var phone = phones.SingleOrDefault(item =>
            string.Equals(item.WabaId, context.WabaId, StringComparison.Ordinal)
            && string.Equals(item.PhoneNumberId, context.PhoneNumberId, StringComparison.Ordinal));
        if (phone is null || phone.RegistrationStatus != MetaPhoneRegistrationStatus.Registered)
            return null;

        var operational = (await conversacionesConfig.GetWhatsAppNumerosAsync(ct))
            .Any(item => item.Activo && string.Equals(item.PhoneNumberId, phone.PhoneNumberId, StringComparison.Ordinal));
        return new WhatsAppEmbeddedRecoveryCandidate(
            onboarding.IdOnboarding,
            phone.PhoneNumberId,
            phone.VerifiedName,
            phone.DisplayPhoneNumber,
            operational);
    }

    public async Task<WhatsAppEmbeddedOperationalImportResult> RecoverAsync(Guid idOnboarding, CancellationToken ct = default)
    {
        var candidate = await GetRecoveryCandidateAsync(ct)
            ?? throw new InvalidOperationException("No se encontró un WhatsApp autorizado que pueda recuperarse.");
        if (candidate.IdOnboarding != idOnboarding)
            throw new InvalidOperationException("El onboarding recuperable cambió. Actualizá la pantalla antes de continuar.");

        if (!candidate.IsOperational)
        {
            await conversacionesConfig.SaveWhatsAppNumeroAsync(new ConversacionWhatsAppNumeroDto
            {
                PhoneNumberId = candidate.PhoneNumberId,
                Nombre = candidate.Nombre,
                Activo = true,
                Usuarios = []
            }, ct);
        }

        var matches = (await conversacionesConfig.GetWhatsAppNumerosAsync(ct))
            .Where(item => string.Equals(item.PhoneNumberId, candidate.PhoneNumberId, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || !matches[0].Activo)
            throw new InvalidOperationException("La recuperación no produjo un único número operativo.");

        return new WhatsAppEmbeddedOperationalImportResult([
            new WhatsAppEmbeddedOperationalImportedNumber(
                matches[0].IdNumero,
                matches[0].Nombre,
                candidate.DisplayPhoneNumber,
                candidate.PhoneNumberId,
                string.Empty)
        ]);
    }

    private async Task<IReadOnlyList<AuthorizedPhoneForImport>> DiscoverAuthorizedPhonesAsync(
        WhatsAppVaultSecretContext context,
        WhatsAppCredentialReference tokenReference,
        CancellationToken ct)
    {
        var result = new List<AuthorizedPhoneForImport>();
        if (!string.IsNullOrWhiteSpace(context.WabaId))
        {
            var knownWabaPhones = await managementClient.DiscoverPhoneNumbersAsync(context.WabaId, tokenReference, ct);
            result.AddRange(knownWabaPhones.Select(phone => new AuthorizedPhoneForImport(
                context.MetaBusinessId,
                context.WabaId,
                phone.PhoneNumberId,
                phone.DisplayPhoneNumber,
                phone.VerifiedName,
                phone.RegistrationStatus)));
            if (result.Count > 0)
                return result
                    .GroupBy(phone => phone.PhoneNumberId, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();
        }

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
                result.AddRange(phones.Select(phone => new AuthorizedPhoneForImport(
                    business.BusinessId,
                    waba.WabaId,
                    phone.PhoneNumberId,
                    phone.DisplayPhoneNumber,
                    phone.VerifiedName,
                    phone.RegistrationStatus)));
            }
        }

        return result
            .GroupBy(phone => phone.PhoneNumberId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private sealed record AuthorizedPhoneForImport(
        string MetaBusinessId,
        string WabaId,
        string PhoneNumberId,
        string DisplayPhoneNumber,
        string VerifiedName,
        MetaPhoneRegistrationStatus RegistrationStatus);

    private sealed record PendingConnectionMetadata(
        string PhoneNumberId,
        string Nombre,
        string DisplayPhoneNumber);
}
