using AlfaCore.Models;
using AlfaCore.Configuration;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed record WhatsAppEmbeddedOperationalImportResult(int IdNumero, string Nombre, string DisplayPhoneNumber);
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
    IOptions<WhatsAppEmbeddedSignupOptions> options)
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public async Task<WhatsAppEmbeddedOperationalImportResult> CompleteAsync(Guid idOnboarding, CancellationToken ct = default)
    {
        var activeBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
        if (!_options.IsAllowedForBase(activeBaseId))
            throw new UnauthorizedAccessException("Embedded Signup no está habilitado para esta base.");
        var onboarding = await store.GetAsync(idOnboarding, ct)
            ?? throw new InvalidOperationException("El onboarding no existe.");
        if (activeBaseId <= 0 || onboarding.IdBase != activeBaseId)
            throw new UnauthorizedAccessException("El onboarding no pertenece a la base activa.");
        if (onboarding.OnboardingMode != WhatsAppEmbeddedOnboardingMode.Standard
            || onboarding.Status != WhatsAppEmbeddedOnboardingStatus.Importing
            || onboarding.CurrentStep != "READY_FOR_OPERATIONAL_UPSERT")
            throw new InvalidOperationException("El onboarding no está listo para el alta operativa.");

        var tokenReference = new WhatsAppCredentialReference(onboarding.TokenReference.Trim());
        var context = await credentialVault.GetContextAsync(tokenReference, ct)
            ?? throw new InvalidOperationException("La credencial segura no tiene contexto vigente.");
        if (context.IdBase != activeBaseId || context.IdOnboarding != idOnboarding
            || string.IsNullOrWhiteSpace(context.WabaId) || string.IsNullOrWhiteSpace(context.PhoneNumberId))
            throw new InvalidOperationException("El contexto seguro no coincide con el onboarding.");

        var phones = await managementClient.DiscoverPhoneNumbersAsync(context.WabaId, tokenReference, ct);
        var phone = phones.SingleOrDefault(x => x.PhoneNumberId == context.PhoneNumberId && x.WabaId == context.WabaId)
            ?? throw new InvalidOperationException("Meta no devolvió exactamente el teléfono autorizado.");
        if (phone.RegistrationStatus != MetaPhoneRegistrationStatus.Registered)
            throw new InvalidOperationException("Meta todavía no confirma el teléfono como registrado.");

        var wabaOwnership = await ownershipStore.ReserveWabaAsync(phone.WabaId, activeBaseId, context.MetaBusinessId, ct);
        var phoneOwnership = await ownershipStore.ReservePhoneAsync(phone.PhoneNumberId, phone.WabaId, activeBaseId, ct);
        if (wabaOwnership.Result == WhatsAppAssetOwnershipResult.Conflict
            || phoneOwnership.Result == WhatsAppAssetOwnershipResult.Conflict)
            throw new UnauthorizedAccessException("El activo de WhatsApp pertenece a otra base.");

        await conversacionesConfig.SaveWhatsAppNumeroAsync(new ConversacionWhatsAppNumeroDto
        {
            PhoneNumberId = phone.PhoneNumberId,
            Nombre = phone.VerifiedName,
            Activo = true,
            Usuarios = []
        }, ct);

        var saved = (await conversacionesConfig.GetWhatsAppNumerosAsync(ct))
            .Single(x => x.PhoneNumberId == phone.PhoneNumberId);
        await store.MarkReadyAsync(idOnboarding, ct);
        return new WhatsAppEmbeddedOperationalImportResult(saved.IdNumero, saved.Nombre, phone.DisplayPhoneNumber);
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

        return new WhatsAppEmbeddedOperationalImportResult(
            matches[0].IdNumero,
            matches[0].Nombre,
            candidate.DisplayPhoneNumber);
    }
}
