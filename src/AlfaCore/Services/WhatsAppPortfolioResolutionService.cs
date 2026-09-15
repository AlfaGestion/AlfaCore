using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Completa MetaBusinessId/WabaId/nombre de portfolio para números que quedaron sin ese dato --
/// típicamente números conectados ANTES de que existiera esta funcionalidad. Dos mecanismos, ninguno
/// con una llamada a Meta por render:
///
/// 1) BackfillNumeroMetaIdentityAsync: reconstruye MetaBusinessId/WabaId desde el ownership CENTRAL ya
///    persistido (WhatsAppPhoneOwnership -> WhatsAppWabaOwnership). Nunca llama a Meta -- sólo son dos
///    lecturas a ALFA_CENTRAL. Si el número nunca pasó por Embedded Signup no hay ownership y no hay
///    nada que reconstruir (queda "sin portfolio" para siempre, correctamente).
///
/// 2) TryResolvePortfolioNameAsync: para un MetaBusinessId YA conocido sin nombre cacheado, reserva
///    atómicamente un intento (WhatsAppEmbeddedSignupOptions.PortfolioResolutionThrottle, default 24h)
///    vía IConversacionesConfigService.TryReserveResolutionAttemptAsync -- sólo si se reserva llama a
///    IMetaWhatsAppManagementClient.GetBusinessNameAsync, usando el WhatsAppRuntimeCredential ya
///    resuelto para envío de mensajes (nunca el Vault de onboarding, que ya no tiene contexto vigente
///    para un número que quedó operativo hace tiempo).
///
/// Ambos métodos son best-effort: nunca lanzan, nunca bloquean la pantalla de Configuración si Meta o
/// el ownership central no responden.
/// </summary>
public sealed record WhatsAppNumeroMetaIdentity(string MetaBusinessId, string WabaId);

public interface IWhatsAppPortfolioResolutionService
{
    /// <summary>Devuelve la identidad reconstruida (ya persistida) para que el llamador pueda reflejarla
    /// en el DTO ya cargado sin un round-trip extra -- null si no había ownership para reconstruir.</summary>
    Task<WhatsAppNumeroMetaIdentity?> BackfillNumeroMetaIdentityAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default);
    Task TryResolvePortfolioNameAsync(int idBase, string metaBusinessId, string representativePhoneNumberId, CancellationToken ct = default);
}

public sealed class WhatsAppPortfolioResolutionService(
    IConversacionesConfigService conversacionesConfig,
    IWhatsAppAssetOwnershipStore ownershipStore,
    IMetaWhatsAppManagementClient managementClient,
    IWhatsAppRuntimeCredentialResolver credentialResolver,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IWhatsAppPortfolioResolutionService
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public async Task<WhatsAppNumeroMetaIdentity?> BackfillNumeroMetaIdentityAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default)
    {
        if (idBase <= 0 || numero is null || !string.IsNullOrWhiteSpace(numero.MetaBusinessId))
            return null; // Ya tiene identidad conocida -- nada para reconstruir (self-throttling: una
                          // vez backfillado, nunca vuelve a entrar acá).

        try
        {
            var phoneOwnership = await ownershipStore.GetPhoneOwnershipAsync(numero.PhoneNumberId, ct);
            if (phoneOwnership is null || phoneOwnership.IdBase != idBase)
                return null; // Nunca pasó por Embedded Signup (o pertenece a otra base -- no debería,
                              // pero nunca se confía ciegamente): UNKNOWN para siempre, correctamente.

            var wabaOwnership = await ownershipStore.GetWabaOwnershipAsync(phoneOwnership.WabaId, ct);
            if (wabaOwnership is null)
                return null;

            await conversacionesConfig.BackfillNumeroMetaIdentityAsync(numero.IdNumero, wabaOwnership.MetaBusinessId, phoneOwnership.WabaId, ct);
            return new WhatsAppNumeroMetaIdentity(wabaOwnership.MetaBusinessId, phoneOwnership.WabaId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // Best-effort -- el próximo refresh de la lista puede reintentar.
        }
    }

    public async Task TryResolvePortfolioNameAsync(int idBase, string metaBusinessId, string representativePhoneNumberId, CancellationToken ct = default)
    {
        var businessId = (metaBusinessId ?? string.Empty).Trim();
        if (idBase <= 0 || businessId.Length == 0)
            return;

        try
        {
            var claimed = await conversacionesConfig.TryReserveResolutionAttemptAsync(idBase, businessId, _options.PortfolioResolutionThrottle, ct);
            if (!claimed)
                return; // Ya resuelto, o alguien más ya reservó esta ventana -- nunca llamar a Meta acá.

            var legacyConfig = await conversacionesConfig.GetWhatsAppConfigAsync(ct);
            var credential = await credentialResolver.ResolveAsync(idBase, null, representativePhoneNumberId, legacyConfig, ct);
            var name = await managementClient.GetBusinessNameAsync(businessId, credential.AccessToken, credential.GraphVersion, ct);
            if (!string.IsNullOrWhiteSpace(name))
                await conversacionesConfig.SetPortfolioNameAsync(idBase, businessId, name, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort -- ya se registró el intento (throttle), así que esto no reintenta hasta la
            // próxima ventana. Nunca debe poder romper la pantalla de Configuración.
        }
    }
}
