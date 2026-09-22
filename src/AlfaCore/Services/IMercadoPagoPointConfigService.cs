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

    /// <summary>Código de dbo.MA_CUENTAS (el mismo Codigo que ya usa el selector de medios de pago del
    /// POS) que dispara el cobro real con la terminal. No hay ninguna convención fija tipo "MPPOINT":
    /// cada cliente ya tiene su propio código para Mercado Pago/QR (ej. "QR", "MP") con su propio
    /// MedioDePago de clasificación contable (normalmente "EF") -- este valor es el único vínculo entre
    /// ese medio de pago y la integración. Vacío = la integración está configurada pero todavía no
    /// vinculada a ningún medio de pago del POS.</summary>
    Task<string> ResolveCodigoMedioPagoAsync(CancellationToken ct = default);

    /// <summary>Para la herramienta de diagnóstico (Fase 1) -- guarda las 5 claves en
    /// TA_CONFIGURACION. Solo superadmin/admin de la base, sin distinción de rol por ahora (se llama
    /// desde una pantalla ya restringida por el layout general de la app).</summary>
    Task GuardarConfiguracionAsync(string accessToken, string terminalId, string posExternalId, string webhookSecret, string codigoMedioPago, CancellationToken ct = default);
}
