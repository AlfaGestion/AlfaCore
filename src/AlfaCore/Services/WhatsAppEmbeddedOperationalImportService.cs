using AlfaCore.Models;
using AlfaCore.Configuration;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed record WhatsAppEmbeddedOperationalImportResult(int IdNumero, string Nombre, string DisplayPhoneNumber);

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
}
