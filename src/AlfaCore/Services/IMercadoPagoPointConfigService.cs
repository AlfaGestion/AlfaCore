using AlfaCore.Services.MercadoPagoPoint;

namespace AlfaCore.Services;

public interface IMercadoPagoPointConfigService
{
    /// <summary>Null si no hay AccessToken/TerminalId configurados para la base activa (módulo
    /// apagado/no configurado todavía).</summary>
    Task<MercadoPagoPointOptions?> ResolveOptionsAsync(CancellationToken ct = default);

    /// <summary>Secreto para validar la firma HMAC del webhook (header x-signature). Vacío si no está
    /// configurado -- en ese caso el webhook rechaza cualquier notificación (nunca se salta la
    /// validación en producción).</summary>
    Task<string> ResolveWebhookSecretAsync(CancellationToken ct = default);

    /// <summary>Solo informativo (no lo usa ninguna llamada a la API), para mostrarlo en la pantalla
    /// de configuración.</summary>
    Task<string> ResolvePosExternalIdAsync(CancellationToken ct = default);

    /// <summary>Para la herramienta de diagnóstico (Fase 1) -- guarda las 4 claves en
    /// TA_CONFIGURACION. Solo superadmin/admin de la base, sin distinción de rol por ahora (se llama
    /// desde una pantalla ya restringida por el layout general de la app).</summary>
    Task GuardarConfiguracionAsync(string accessToken, string terminalId, string posExternalId, string webhookSecret, CancellationToken ct = default);
}
