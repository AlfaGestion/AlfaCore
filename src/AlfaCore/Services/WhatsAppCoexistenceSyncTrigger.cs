using AlfaCore.Models;
using Microsoft.Extensions.Logging;

namespace AlfaCore.Services;

public sealed class WhatsAppCoexistenceSyncTrigger(
    IWhatsAppCoexistenceSyncStore syncStore,
    IMetaWhatsAppManagementClient managementClient,
    ILogger<WhatsAppCoexistenceSyncTrigger> logger) : IWhatsAppCoexistenceSyncTrigger
{
    private static readonly TimeSpan SyncWindow = TimeSpan.FromHours(24);

    // Orden recomendado por Meta: contactos primero, historial después. Son independientes -- si uno
    // falla (POST o persistencia) el otro se intenta igual.
    private static readonly WhatsAppCoexistenceSyncType[] OrderedSyncTypes =
        [WhatsAppCoexistenceSyncType.ContactState, WhatsAppCoexistenceSyncType.History];

    public async Task TriggerInitialSyncsAsync(
        WhatsAppEmbeddedOnboardingDto onboarding,
        IReadOnlyList<WhatsAppCoexistencePhoneCandidate> phones,
        WhatsAppCredentialReference tokenReference,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        ArgumentNullException.ThrowIfNull(phones);

        // Standard nunca dispara nada acá -- sólo Coexistence tiene history/smb_app_state_sync.
        if (onboarding.OnboardingMode != WhatsAppEmbeddedOnboardingMode.BusinessAppCoexistence)
            return;

        var nowUtc = DateTime.UtcNow;
        var windowExpired = nowUtc - onboarding.StartedAtUtc > SyncWindow;

        foreach (var phone in phones)
        {
            if (!phone.IsOnBizApp)
                continue; // Meta no informó este número como vinculado a la app -- no hay nada que sincronizar.
            var phoneNumberId = (phone.PhoneNumberId ?? string.Empty).Trim();
            if (phoneNumberId.Length == 0)
                continue;

            foreach (var syncType in OrderedSyncTypes)
            {
                try
                {
                    await TriggerOneAsync(onboarding, phoneNumberId, syncType, tokenReference, windowExpired, nowUtc, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Cada (número, tipo) es independiente: un fallo nunca debe impedir el otro tipo,
                    // ni el siguiente número, ni -- sobre todo -- revertir o bloquear el READY que ya
                    // se confirmó antes de llamar acá.
                    logger.LogWarning(ex, "No se pudo disparar el sync inicial {SyncType} de Coexistence para {PhoneNumberId} (onboarding {IdOnboarding}).",
                        syncType, phoneNumberId, onboarding.IdOnboarding);
                }
            }
        }
    }

    private async Task TriggerOneAsync(
        WhatsAppEmbeddedOnboardingDto onboarding,
        string phoneNumberId,
        WhatsAppCoexistenceSyncType syncType,
        WhatsAppCredentialReference tokenReference,
        bool windowExpired,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var reserved = await syncStore.TryReserveAsync(
            onboarding.IdBase, phoneNumberId, onboarding.IdOnboarding, syncType,
            windowExpired ? WhatsAppCoexistenceSyncStatus.Expired : WhatsAppCoexistenceSyncStatus.Pending,
            nowUtc, ct);
        if (!reserved)
            return; // Ya existe una fila para este número/tipo: one-shot, nunca se repite automáticamente.
        if (windowExpired)
            return; // La fila quedó registrada como Expired -- jamás se llama a Meta.

        try
        {
            var result = await managementClient.RequestSmbAppDataSyncAsync(phoneNumberId, syncType, tokenReference, ct);
            await syncStore.MarkRequestedAsync(onboarding.IdBase, phoneNumberId, syncType, result.RequestId, DateTime.UtcNow, ct);
        }
        catch (Exception ex)
        {
            var errorCode = (ex as MetaWhatsAppManagementException)?.ErrorCode ?? "SMB_APP_DATA_REQUEST_FAILED";
            await syncStore.MarkFailedAsync(onboarding.IdBase, phoneNumberId, syncType, errorCode, "No se pudo solicitar el sync inicial con Meta.", ct);
            throw;
        }
    }
}
