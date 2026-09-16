using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

/// <summary>
/// Completa MetaBusinessId/WabaId/nombre de portfolio para números que quedaron sin ese dato --
/// típicamente números conectados ANTES de que existiera esta funcionalidad, agregados manualmente por
/// Phone Number ID (sin ownership central), o con "ownership central incompleto" (WabaId conocido,
/// MetaBusinessId nunca capturado -- caso real encontrado en Base4264/Alfa Claro 1). Ningún mecanismo
/// llama a Meta por render.
///
/// BackfillNumeroMetaIdentityAsync reconstruye lo que YA se sabe, sin Meta, en orden creciente de costo:
///   1) Si el número ya tiene WabaId (de un backfill anterior): sólo falta el Business -- lee la caché
///      tenant CONV_WHATSAPP_WABA_BUSINESS_MAP (barata, nunca central, nunca Meta). Evita repetir el
///      ownership central en cada refresh una vez que la WABA ya se conoce.
///   2) Si no, prueba el ownership CENTRAL (WhatsAppPhoneOwnership -> WhatsAppWabaOwnership, ALFA_CENTRAL)
///      -- si existe, siempre backfillea el WabaId, y el MetaBusinessId sólo si no vino vacío. Un
///      ownership central con MetaBusinessId vacío es RESOLVABLE, no un fallo silencioso: se devuelve un
///      identity explícito con el WabaId conocido para que el llamador lo persista y la UI lo refleje.
///   3) Si tampoco hay ownership central (manual real), prueba la WABA legacy de la base
///      (ConversacionWhatsAppConfigDto.BusinessAccountId) contra la misma caché tenant.
///
/// TryResolveWabaOwningBusinessAsync es el único lugar que llama a Meta (GetWabaOwningBusinessIdAsync,
/// GET /{wabaId}?fields=owner_business_info) para una WABA sin Business cacheado -- throttled
/// (WhatsAppEmbeddedSignupOptions.PortfolioResolutionThrottle) y compartido: da igual si la WABA es de
/// un número con ownership central incompleto o la legacy de varios números manuales, el mecanismo es
/// el mismo y la reserva es por WabaId, así que 5 números con la misma WABA producen un solo GET
/// efectivo por ventana. Usa WhatsAppRuntimeCredential (el mismo credential de envío de mensajes) --
/// nunca el Vault de onboarding, nunca un token legacy arbitrario. Si Meta resuelve el Business, PRIMERO
/// intenta reparar el ownership CENTRAL (WhatsAppWabaOwnership.MetaBusinessId -- ver
/// IWhatsAppAssetOwnershipStore.TryRepairWabaMetaBusinessIdAsync) y sólo si es consistente (vacío
/// reparado, o ya coincidía, o la WABA nunca se reservó centralmente -- legacy) recién ahí cachea
/// tenant-side. Si el central ya tenía OTRO valor no vacío (Conflict, una inconsistencia real): fail
/// closed total -- ni se pisa central ni se persiste el valor de Meta en ningún lado tenant (ni
/// CONV_WHATSAPP_NUMEROS ni CONV_WHATSAPP_WABA_BUSINESS_MAP), sólo se deja constancia sanitizada para
/// diagnóstico (IAppEventService.LogAuditAsync). El orden importa: cachear tenant ANTES de chequear el
/// central dejaría central=A y tenant=B -- inconsistente, no fail-closed.
///
/// TryResolvePortfolioNameAsync resuelve el NOMBRE para un MetaBusinessId ya conocido -- mismo patrón de
/// throttle/credential, sin cambios respecto de antes.
///
/// Los tres métodos son best-effort: nunca lanzan (incluida la credencial segura indisponible en este
/// proceso -- ver WhatsAppEmbeddedVaultUnavailableException/CryptographicException), nunca bloquean la
/// pantalla de Configuración, nunca borran un dato ya cacheado ante un fallo de Meta.
/// </summary>
public sealed record WhatsAppNumeroMetaIdentity(string MetaBusinessId, string WabaId);

public interface IWhatsAppPortfolioResolutionService
{
    /// <summary>Devuelve la identidad reconstruida (ya persistida) para que el llamador pueda reflejarla
    /// en el DTO ya cargado sin un round-trip extra. MetaBusinessId puede venir vacío en el resultado --
    /// eso es "WabaId conocido, Business todavía pendiente" (RESOLVABLE), no un error. Null sólo cuando
    /// no hay absolutamente nada para reconstruir (UNKNOWN real).</summary>
    Task<WhatsAppNumeroMetaIdentity?> BackfillNumeroMetaIdentityAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default);
    Task TryResolvePortfolioNameAsync(int idBase, string metaBusinessId, string representativePhoneNumberId, CancellationToken ct = default);
    /// <summary>Resuelve, throttled, qué Business es dueño de una WABA sin Business cacheado todavía --
    /// tanto para la WABA legacy de números manuales como para la WABA de un número con ownership
    /// central incompleto. Compartido por WabaId: varios números con la misma WABA producen un solo GET
    /// efectivo por ventana de throttle.</summary>
    Task TryResolveWabaOwningBusinessAsync(int idBase, string wabaId, string representativePhoneNumberId, CancellationToken ct = default);
}

public sealed class WhatsAppPortfolioResolutionService(
    IConversacionesConfigService conversacionesConfig,
    IWhatsAppAssetOwnershipStore ownershipStore,
    IMetaWhatsAppManagementClient managementClient,
    IWhatsAppRuntimeCredentialResolver credentialResolver,
    IOptions<WhatsAppEmbeddedSignupOptions> options,
    IAppEventService appEvents) : IWhatsAppPortfolioResolutionService
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public async Task<WhatsAppNumeroMetaIdentity?> BackfillNumeroMetaIdentityAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default)
    {
        if (idBase <= 0 || numero is null || !string.IsNullOrWhiteSpace(numero.MetaBusinessId))
            return null; // Ya tiene identidad completa -- nada para reconstruir (self-throttling: una
                          // vez que el número tiene MetaBusinessId, nunca vuelve a entrar acá).

        try
        {
            // Ya sabemos la WABA de un backfill anterior -- sólo falta el Business. Nunca vuelve a
            // consultar el ownership central para esto: evita el "reintento central inútil en cada
            // refresh" -- lo único que puede haber cambiado es la caché tenant (otro número con la misma
            // WABA ya la resolvió) o, indirectamente, el central reparado (que ya se reflejó en la caché
            // tenant en el momento en que se reparó).
            if (!string.IsNullOrWhiteSpace(numero.WabaId))
                return await TryFromWabaBusinessCacheAsync(idBase, numero, numero.WabaId, ct);

            var fromCentralOwnership = await TryFromCentralOwnershipAsync(idBase, numero, ct);
            if (fromCentralOwnership is not null)
                return fromCentralOwnership;

            var legacyConfig = await conversacionesConfig.GetWhatsAppConfigAsync(idBase, ct);
            var legacyWabaId = (legacyConfig.BusinessAccountId ?? string.Empty).Trim();
            return legacyWabaId.Length == 0
                ? null // Ni ownership central ni WABA legacy -- UNKNOWN real, no hay nada que reconstruir.
                : await TryFromWabaBusinessCacheAsync(idBase, numero, legacyWabaId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // Best-effort -- el próximo refresh de la lista puede reintentar.
        }
    }

    /// <summary>
    /// Ownership central (WhatsAppPhoneOwnership -> WhatsAppWabaOwnership, ALFA_CENTRAL) -- nunca llama a
    /// Meta, sólo dos lecturas. Si existe, SIEMPRE backfillea el WabaId conocido, aunque el
    /// MetaBusinessId central esté vacío ("ownership central incompleto" -- caso real: Base4264/Alfa
    /// Claro 1, WhatsAppWabaOwnership existe pero su MetaBusinessId nunca se capturó). Ese caso es
    /// RESOLVABLE, no un fallo silencioso -- se devuelve un identity explícito con MetaBusinessId vacío
    /// y WabaId real para que el llamador lo persista y la UI muestre "pendiente de identificar" en vez
    /// de ocultar la línea como si no hubiera portfolio en absoluto.
    /// </summary>
    private async Task<WhatsAppNumeroMetaIdentity?> TryFromCentralOwnershipAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct)
    {
        var phoneOwnership = await ownershipStore.GetPhoneOwnershipAsync(numero.PhoneNumberId, ct);
        if (phoneOwnership is null || phoneOwnership.IdBase != idBase)
            return null; // Nunca pasó por Embedded Signup (o pertenece a otra base -- no debería, pero
                          // nunca se confía ciegamente): esta vía no tiene nada, probar la legacy.

        var wabaOwnership = await ownershipStore.GetWabaOwnershipAsync(phoneOwnership.WabaId, ct);
        if (wabaOwnership is null)
            return null;

        var metaBusinessId = (wabaOwnership.MetaBusinessId ?? string.Empty).Trim();
        await conversacionesConfig.BackfillNumeroMetaIdentityAsync(numero.IdNumero, metaBusinessId, phoneOwnership.WabaId, idBase, ct);
        return new WhatsAppNumeroMetaIdentity(metaBusinessId, phoneOwnership.WabaId);
    }

    /// <summary>
    /// Sólo LEE la caché tenant CONV_WHATSAPP_WABA_BUSINESS_MAP para una WABA ya conocida (nunca llama a
    /// Meta acá -- eso es TryResolveWabaOwningBusinessAsync, throttled). Sirve tanto para la WABA legacy
    /// de un número manual como para la WABA de un número con ownership central incompleto -- en ambos
    /// casos SIEMPRE backfillea el WabaId (ya lo sabíamos), y el Business sólo si ya está cacheado.
    /// </summary>
    private async Task<WhatsAppNumeroMetaIdentity?> TryFromWabaBusinessCacheAsync(int idBase, ConversacionWhatsAppNumeroDto numero, string wabaId, CancellationToken ct)
    {
        var normalizedWabaId = (wabaId ?? string.Empty).Trim();
        if (normalizedWabaId.Length == 0)
            return null;

        var map = await conversacionesConfig.GetWabaBusinessMapAsync([normalizedWabaId], idBase, ct);
        var businessId = map.TryGetValue(normalizedWabaId, out var cached) ? (cached ?? string.Empty).Trim() : string.Empty;

        await conversacionesConfig.BackfillNumeroMetaIdentityAsync(numero.IdNumero, businessId, normalizedWabaId, idBase, ct);
        return new WhatsAppNumeroMetaIdentity(businessId, normalizedWabaId);
    }

    public async Task TryResolveWabaOwningBusinessAsync(int idBase, string wabaId, string representativePhoneNumberId, CancellationToken ct = default)
    {
        var normalizedWabaId = (wabaId ?? string.Empty).Trim();
        if (idBase <= 0 || normalizedWabaId.Length == 0)
            return;

        try
        {
            var claimed = await conversacionesConfig.TryReserveWabaResolutionAttemptAsync(idBase, normalizedWabaId, _options.PortfolioResolutionThrottle, ct);
            if (!claimed)
                return; // Ya resuelto, o alguien más ya reservó esta ventana -- nunca llamar a Meta acá.

            // La credencial puede no poder abrirse EN ESTE PROCESO (p. ej. local contra un ownership
            // central real, sin el certificado de Data Protection del servidor) -- eso cae en el mismo
            // catch de abajo: nunca rompe Configuración, nunca toca la caché, el intento YA quedó
            // registrado arriba así que no se reintenta hasta la próxima ventana. Nunca copiar
            // certificados ni agregar un fallback inseguro para evitar esto.
            var legacyConfig = await conversacionesConfig.GetWhatsAppConfigAsync(idBase, ct);
            var credential = await credentialResolver.ResolveAsync(idBase, null, representativePhoneNumberId, legacyConfig, ct);
            var businessId = await managementClient.GetWabaOwningBusinessIdAsync(normalizedWabaId, credential.AccessToken, credential.GraphVersion, ct);
            if (string.IsNullOrWhiteSpace(businessId))
                return;

            // El central es SIEMPRE lo primero que se consulta, antes de tocar nada tenant -- si el
            // central ya tiene un MetaBusinessId distinto (Conflict), NO se persiste el valor de Meta ni
            // acá ni en CONV_WHATSAPP_NUMEROS/CONV_WHATSAPP_WABA_BUSINESS_MAP, ni se resuelve el nombre
            // del portfolio con él. Escribir tenant primero (como hacía antes) dejaba central=A y
            // tenant=B -- una inconsistencia real, no un fail-closed. Repaired/AlreadyMatches/NotFound
            // (WABA nunca reservada centralmente -- típico de la legacy de un número manual) sí son
            // consistentes: recién ahí se completa la caché tenant.
            var repairResult = await ownershipStore.TryRepairWabaMetaBusinessIdAsync(normalizedWabaId, idBase, businessId, ct);
            if (repairResult == WhatsAppWabaMetaBusinessRepairResult.Conflict)
            {
                await LogWabaMetaBusinessConflictAsync(idBase, normalizedWabaId, businessId, ct);
                return; // El número queda RESOLVABLE/pendiente -- nunca UNKNOWN -- con lo que ya
                        // hubiera cacheado antes (si algo), sin tocar nada nuevo.
            }

            await conversacionesConfig.SetWabaOwningBusinessIdAsync(idBase, normalizedWabaId, businessId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort -- ya se registró el intento (throttle). Nunca debe poder romper Configuración.
        }
    }

    /// <summary>
    /// Diagnóstico interno, sanitizado -- sólo ids técnicos de Meta (Waba/Business), nunca tokens ni
    /// datos de cliente. Nunca lanza: si el logueo en sí fallara, el fail-closed de arriba ya se
    /// cumplió igual (nada se sobrescribió, nada se persistió mal) y no hace falta romper la pantalla
    /// sólo porque no se pudo dejar constancia.
    /// </summary>
    private async Task LogWabaMetaBusinessConflictAsync(int idBase, string wabaId, string resolvedMetaBusinessId, CancellationToken ct)
    {
        try
        {
            await appEvents.LogAuditAsync(
                "WhatsAppPortfolio",
                "WabaMetaBusinessConflict",
                "WhatsAppWabaOwnership",
                wabaId,
                "El ownership central ya tiene un MetaBusinessId distinto del que Meta devolvió ahora para esta WABA -- fail closed, no se sobrescribió ni se persistió tenant-side.",
                new { IdBase = idBase, WabaId = wabaId, MetaResolvedBusinessId = resolvedMetaBusinessId },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Dejar constancia es best-effort -- nunca debe poder romper Configuración.
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

            var legacyConfig = await conversacionesConfig.GetWhatsAppConfigAsync(idBase, ct);
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
