using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Completa MetaBusinessId/WabaId/nombre de portfolio para números que quedaron sin ese dato --
/// típicamente números conectados ANTES de que existiera esta funcionalidad, o agregados manualmente
/// por Phone Number ID (sin ownership central). Tres mecanismos, ninguno con una llamada a Meta por
/// render:
///
/// 1) BackfillNumeroMetaIdentityAsync: reconstruye MetaBusinessId/WabaId, en orden:
///    (a) desde el ownership CENTRAL ya persistido (WhatsAppPhoneOwnership -> WhatsAppWabaOwnership) --
///        nunca llama a Meta, sólo dos lecturas a ALFA_CENTRAL;
///    (b) si no hay ownership central (típico de un número agregado manualmente), desde la caché local
///        WABA->Business (CONV_WHATSAPP_WABA_BUSINESS_MAP) para la WABA legacy de la base
///        (ConversacionWhatsAppConfigDto.BusinessAccountId) -- también sin llamar a Meta, sólo lee una
///        caché ya resuelta por TryResolveLegacyWabaOwningBusinessAsync.
///    "Manual" NO es sinónimo de "Portfolio siempre desconocido": sólo lo es si ni el ownership central
///    ni la WABA legacy existen -- ver WhatsAppPortfolioResolutionStatus.Unknown.
///
/// 2) TryResolveLegacyWabaOwningBusinessAsync: para la WABA legacy de la base (compartida por todos los
///    números manuales -- Meta Cloud API legacy no soporta más de una WABA por token) sin Business
///    cacheado todavía: reserva un intento throttled y, si se reserva, llama a
///    IMetaWhatsAppManagementClient.GetWabaOwningBusinessIdAsync con el mismo WhatsAppRuntimeCredential
///    legacy que ya se usa para enviar mensajes por esos números -- nunca una integración nueva.
///
/// 3) TryResolvePortfolioNameAsync: para un MetaBusinessId YA conocido (de cualquiera de los dos
///    orígenes de arriba) sin nombre cacheado, reserva atómicamente un intento
///    (WhatsAppEmbeddedSignupOptions.PortfolioResolutionThrottle, default 24h) y sólo si se reserva llama
///    a GetBusinessNameAsync -- mismo WhatsAppRuntimeCredential, nunca el Vault de onboarding.
///
/// Los tres métodos son best-effort: nunca lanzan, nunca bloquean la pantalla de Configuración si Meta o
/// el ownership central no responden.
/// </summary>
public sealed record WhatsAppNumeroMetaIdentity(string MetaBusinessId, string WabaId);

public interface IWhatsAppPortfolioResolutionService
{
    /// <summary>Devuelve la identidad reconstruida (ya persistida) para que el llamador pueda reflejarla
    /// en el DTO ya cargado sin un round-trip extra -- null si no había nada (ni ownership central ni
    /// caché WABA legacy) para reconstruir.</summary>
    Task<WhatsAppNumeroMetaIdentity?> BackfillNumeroMetaIdentityAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default);
    Task TryResolvePortfolioNameAsync(int idBase, string metaBusinessId, string representativePhoneNumberId, CancellationToken ct = default);
    /// <summary>Sólo relevante para números sin ownership central -- resuelve, throttled, qué Business es
    /// dueño de la WABA legacy de la base (ver BackfillNumeroMetaIdentityAsync).</summary>
    Task TryResolveLegacyWabaOwningBusinessAsync(int idBase, string wabaId, string representativePhoneNumberId, CancellationToken ct = default);
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
            var fromCentralOwnership = await TryFromCentralOwnershipAsync(idBase, numero, ct);
            if (fromCentralOwnership is not null)
                return fromCentralOwnership;

            return await TryFromLegacyWabaCacheAsync(idBase, numero, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // Best-effort -- el próximo refresh de la lista puede reintentar.
        }
    }

    private async Task<WhatsAppNumeroMetaIdentity?> TryFromCentralOwnershipAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct)
    {
        var phoneOwnership = await ownershipStore.GetPhoneOwnershipAsync(numero.PhoneNumberId, ct);
        if (phoneOwnership is null || phoneOwnership.IdBase != idBase)
            return null; // Nunca pasó por Embedded Signup (o pertenece a otra base -- no debería, pero
                          // nunca se confía ciegamente): esta vía no tiene nada, probar la legacy.

        var wabaOwnership = await ownershipStore.GetWabaOwnershipAsync(phoneOwnership.WabaId, ct);
        if (wabaOwnership is null)
            return null;

        await conversacionesConfig.BackfillNumeroMetaIdentityAsync(numero.IdNumero, wabaOwnership.MetaBusinessId, phoneOwnership.WabaId, ct);
        return new WhatsAppNumeroMetaIdentity(wabaOwnership.MetaBusinessId, phoneOwnership.WabaId);
    }

    /// <summary>
    /// Camino para números MANUALES (sin ownership central): su única WABA conocida es la legacy de la
    /// base (compartida por todos -- Cloud API legacy no soporta más de una WABA por token). Sólo LEE la
    /// caché ya resuelta por TryResolveLegacyWabaOwningBusinessAsync -- nunca llama a Meta acá.
    /// </summary>
    private async Task<WhatsAppNumeroMetaIdentity?> TryFromLegacyWabaCacheAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct)
    {
        var legacyConfig = await conversacionesConfig.GetWhatsAppConfigAsync(ct);
        var wabaId = (legacyConfig.BusinessAccountId ?? string.Empty).Trim();
        if (wabaId.Length == 0)
            return null; // Ni siquiera hay una WABA legacy configurada -- Unknown de verdad.

        var map = await conversacionesConfig.GetWabaBusinessMapAsync([wabaId], ct);
        if (!map.TryGetValue(wabaId, out var businessId) || string.IsNullOrWhiteSpace(businessId))
            return null; // Todavía no resuelto -- TryResolveLegacyWabaOwningBusinessAsync se encarga (throttled).

        await conversacionesConfig.BackfillNumeroMetaIdentityAsync(numero.IdNumero, businessId, wabaId, ct);
        return new WhatsAppNumeroMetaIdentity(businessId, wabaId);
    }

    public async Task TryResolveLegacyWabaOwningBusinessAsync(int idBase, string wabaId, string representativePhoneNumberId, CancellationToken ct = default)
    {
        var normalizedWabaId = (wabaId ?? string.Empty).Trim();
        if (idBase <= 0 || normalizedWabaId.Length == 0)
            return;

        try
        {
            var claimed = await conversacionesConfig.TryReserveWabaResolutionAttemptAsync(idBase, normalizedWabaId, _options.PortfolioResolutionThrottle, ct);
            if (!claimed)
                return; // Ya resuelto, o alguien más ya reservó esta ventana -- nunca llamar a Meta acá.

            var legacyConfig = await conversacionesConfig.GetWhatsAppConfigAsync(ct);
            var credential = await credentialResolver.ResolveAsync(idBase, null, representativePhoneNumberId, legacyConfig, ct);
            var businessId = await managementClient.GetWabaOwningBusinessIdAsync(normalizedWabaId, credential.AccessToken, credential.GraphVersion, ct);
            if (!string.IsNullOrWhiteSpace(businessId))
                await conversacionesConfig.SetWabaOwningBusinessIdAsync(idBase, normalizedWabaId, businessId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort -- ya se registró el intento (throttle). Nunca debe poder romper Configuración.
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
