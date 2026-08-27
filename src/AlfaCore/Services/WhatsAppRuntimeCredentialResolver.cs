using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class WhatsAppWebhookTenantGuard(IWhatsAppAssetOwnershipStore ownershipStore,
    IOptions<WhatsAppEmbeddedSignupOptions>? options = null) : IWhatsAppWebhookTenantGuard
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options?.Value ?? new();

    public async Task ValidateAsync(int currentBaseId, IEnumerable<string> phoneNumberIds, CancellationToken ct = default)
    {
        if (currentBaseId <= 0) throw new InvalidOperationException("El webhook no tiene una base resuelta.");
        if (!await ownershipStore.IsSchemaAvailableAsync(ct))
        {
            if (_options.Enabled) throw new WhatsAppEmbeddedSchemaUnavailableException();
            return;
        }
        foreach (var phoneNumberId in phoneNumberIds.Select(static x => (x ?? string.Empty).Trim()).Where(static x => x.Length > 0).Distinct(StringComparer.Ordinal))
        {
            var ownership = await ownershipStore.GetPhoneOwnershipAsync(phoneNumberId, ct);
            if (ownership is not null && ownership.IdBase != currentBaseId)
                throw new WhatsAppWebhookTenantMismatchException(currentBaseId, ownership.IdBase, phoneNumberId);
        }
    }
}

public sealed class WhatsAppEmbeddedSchemaUnavailableException()
    : InvalidOperationException("El esquema central de WhatsApp Embedded Signup no está disponible. La operación fue detenida de forma segura.");

public sealed class WhatsAppWebhookTenantMismatchException(int callbackBaseId, int ownerBaseId, string phoneNumberId)
    : InvalidOperationException("El Phone Number ID recibido pertenece a otra base. El webhook fue bloqueado antes de persistir datos.")
{
    public int CallbackBaseId { get; } = callbackBaseId;
    public int OwnerBaseId { get; } = ownerBaseId;
    public string PhoneNumberId { get; } = phoneNumberId;
}

public sealed class WhatsAppRuntimeCredentialResolver(IWhatsAppAssetOwnershipStore ownershipStore, IWhatsAppCredentialVault credentialVault,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IWhatsAppRuntimeCredentialResolver
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public async Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default)
    {
        var normalizedPhoneId = (phoneNumberId ?? string.Empty).Trim();
        if (!await ownershipStore.IsSchemaAvailableAsync(ct))
        {
            if (_options.Enabled) throw new WhatsAppEmbeddedSchemaUnavailableException();
            return Legacy(normalizedPhoneId, legacyConfig);
        }
        var ownership = normalizedPhoneId.Length == 0 ? null : await ownershipStore.GetPhoneOwnershipAsync(normalizedPhoneId, ct);
        if (ownership is null)
            return Legacy(normalizedPhoneId, legacyConfig);
        if (ownership.IdBase != idBase) throw new UnauthorizedAccessException("El número de WhatsApp pertenece a otra base.");

        var reference = await credentialVault.FindActiveCredentialAsync(idBase, ownership.WabaId, normalizedPhoneId, ct)
            ?? throw new InvalidOperationException("La credencial segura del número Embedded Signup no está disponible.");
        var secret = await credentialVault.GetAsync(reference, ct);
        if (secret.IsEmpty) throw new InvalidOperationException("La credencial segura del número Embedded Signup está vacía.");
        return new(ownership.WabaId, normalizedPhoneId, _options.GraphApiVersion, secret.ToString(), WhatsAppRuntimeCredentialOrigin.EmbeddedSignup, reference);
    }

    private static WhatsAppRuntimeCredential Legacy(string normalizedPhoneId, ConversacionWhatsAppConfigDto legacyConfig)
        => new(legacyConfig.BusinessAccountId, normalizedPhoneId.Length > 0 ? normalizedPhoneId : legacyConfig.PhoneNumberId,
            legacyConfig.ApiVersion, legacyConfig.AccessToken, WhatsAppRuntimeCredentialOrigin.Legacy);
}
