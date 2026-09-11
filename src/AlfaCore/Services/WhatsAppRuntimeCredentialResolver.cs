using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace AlfaCore.Services;

public sealed class WhatsAppWebhookTenantGuard(IWhatsAppAssetOwnershipStore ownershipStore,
    IOptions<WhatsAppEmbeddedSignupOptions>? options = null) : IWhatsAppWebhookTenantGuard
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options?.Value ?? new();

    public async Task ValidateAsync(int currentBaseId, IEnumerable<string> phoneNumberIds, CancellationToken ct = default)
    {
        if (currentBaseId <= 0) throw new InvalidOperationException("El webhook no tiene una base resuelta.");

        // Ownership central es la autoridad, NO AllowedBaseIds. Con la feature apagada nada ES
        // aplica y el flujo legacy maneja todo.
        if (!_options.Enabled)
            return;

        if (!await ownershipStore.IsSchemaAvailableAsync(ct))
            throw new WhatsAppEmbeddedSchemaUnavailableException();

        var footprint = await ownershipStore.HasEmbeddedSignupFootprintAsync(currentBaseId, ct);

        var normalizedPhoneNumberIds = phoneNumberIds
            .Select(static x => (x ?? string.Empty).Trim())
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedPhoneNumberIds.Length == 0)
        {
            if (footprint)
                throw new WhatsAppWebhookPhoneNumberIdMissingException(currentBaseId);
            return;
        }

        foreach (var phoneNumberId in normalizedPhoneNumberIds)
        {
            var ownership = await ownershipStore.GetPhoneOwnershipAsync(phoneNumberId, ct);
            if (ownership is null)
            {
                // phone sin ownership: si la base tiene footprint ES es un phone desconocido =>
                // fail closed; si no tiene footprint es un asset legacy => passthrough.
                if (footprint)
                    throw new WhatsAppWebhookPhoneOwnershipMissingException(currentBaseId, phoneNumberId);
                continue;
            }
            if (ownership.IdBase != currentBaseId)
                throw new WhatsAppWebhookTenantMismatchException(currentBaseId, ownership.IdBase, phoneNumberId);
        }
    }
}

public sealed class WhatsAppEmbeddedSchemaUnavailableException()
    : InvalidOperationException("El esquema central de WhatsApp Embedded Signup no está disponible. La operación fue detenida de forma segura.");

public sealed class WhatsAppWebhookPhoneNumberIdMissingException(int callbackBaseId)
    : Exception("El webhook no incluye metadata.phone_number_id. Fue bloqueado antes de persistir datos.")
{
    public int CallbackBaseId { get; } = callbackBaseId;
}

public sealed class WhatsAppWebhookPhoneOwnershipMissingException(int callbackBaseId, string phoneNumberId)
    : Exception("El Phone Number ID recibido no tiene ownership central. El webhook fue bloqueado antes de persistir datos.")
{
    public int CallbackBaseId { get; } = callbackBaseId;
    public string PhoneNumberId { get; } = phoneNumberId;
}

public sealed class WhatsAppWebhookTenantMismatchException(int callbackBaseId, int ownerBaseId, string phoneNumberId)
    : Exception("El Phone Number ID recibido pertenece a otra base. El webhook fue bloqueado antes de persistir datos.")
{
    public int CallbackBaseId { get; } = callbackBaseId;
    public int OwnerBaseId { get; } = ownerBaseId;
    public string PhoneNumberId { get; } = phoneNumberId;
}

public sealed class WhatsAppEmbeddedVaultUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed class WhatsAppRuntimeCredentialResolver(IWhatsAppAssetOwnershipStore ownershipStore, IWhatsAppCredentialVault credentialVault,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IWhatsAppRuntimeCredentialResolver
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public async Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default)
    {
        var normalizedPhoneId = (phoneNumberId ?? string.Empty).Trim();

        // La decisión ES-vs-legacy es 100% por ownership central. AllowedBaseIds / AllowAllTenants
        // NO participan acá (sólo gatean iniciar nuevos onboardings). Un asset con ownership ES
        // NUNCA hace fallback a legacy: si algo falta, error controlado.
        if (!await ownershipStore.IsSchemaAvailableAsync(ct))
        {
            if (_options.Enabled) throw new WhatsAppEmbeddedSchemaUnavailableException();
            return Legacy(normalizedPhoneId, legacyConfig);
        }

        var ownership = normalizedPhoneId.Length == 0 ? null : await ownershipStore.GetPhoneOwnershipAsync(normalizedPhoneId, ct);

        if (ownership is null)
            return Legacy(normalizedPhoneId, legacyConfig);                      // sin ownership => legacy

        if (ownership.IdBase != idBase)
            throw new UnauthorizedAccessException("El número de WhatsApp pertenece a otra base.");   // CROSS_TENANT, fail closed

        // ownership.IdBase == idBase  => asset ES autoritativo, NUNCA legacy a partir de acá.
        if (!_options.Enabled)
            throw new WhatsAppEmbeddedVaultUnavailableException(
                "Embedded Signup está deshabilitado en este proceso: la credencial ES de este número no puede resolverse.");   // ES_DISABLED

        if (!_options.HasDataProtectionKeyRingConfiguration())
            throw new WhatsAppEmbeddedVaultUnavailableException("La credencial segura de WhatsApp Embedded Signup no está disponible en este proceso porque falta configurar Data Protection.");

        WhatsAppCredentialReference reference;
        ReadOnlyMemory<char> secret;
        try
        {
            reference = await credentialVault.FindActiveCredentialAsync(idBase, ownership.WabaId, normalizedPhoneId, ct)
                ?? throw new WhatsAppEmbeddedVaultUnavailableException("La credencial segura del número Embedded Signup no está disponible.");
            secret = await credentialVault.GetAsync(reference, ct);
        }
        catch (CryptographicException ex)
        {
            throw new WhatsAppEmbeddedVaultUnavailableException("La credencial segura de WhatsApp Embedded Signup no puede abrirse en este proceso.", ex);
        }

        if (secret.IsEmpty) throw new WhatsAppEmbeddedVaultUnavailableException("La credencial segura del número Embedded Signup está vacía.");
        return new(ownership.WabaId, normalizedPhoneId, _options.GraphApiVersion, secret.ToString(), WhatsAppRuntimeCredentialOrigin.EmbeddedSignup, reference);
    }

    private static WhatsAppRuntimeCredential Legacy(string normalizedPhoneId, ConversacionWhatsAppConfigDto legacyConfig)
        => new(legacyConfig.BusinessAccountId, normalizedPhoneId.Length > 0 ? normalizedPhoneId : legacyConfig.PhoneNumberId,
            legacyConfig.ApiVersion, legacyConfig.AccessToken, WhatsAppRuntimeCredentialOrigin.Legacy);
}
