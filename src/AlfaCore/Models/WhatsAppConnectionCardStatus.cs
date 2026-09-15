namespace AlfaCore.Models;

using AlfaCore.Components.Shared.AlfaDesign;

/// <summary>
/// Estado unificado de una fila ("tarjeta") en la lista "WhatsApp conectados", sea un número ya
/// operativo o una conexión pendiente/fallida de Embedded Signup. Jerarquía explícita pedida en la
/// auditoría de la Sección 9: BLOCKED > ACTION_REQUIRED > FAILED > CONFIGURING > READY_WITH_NO_USERS
/// > READY -- el valor del enum ES esa prioridad (mayor valor = gana).
/// </summary>
public enum WhatsAppConnectionCardStatus
{
    Ready = 0,
    ReadyWithNoUsers = 1,
    Configuring = 2,
    Failed = 3,
    ActionRequired = 4,
    Blocked = 5,
}

/// <summary>
/// Resuelve el <see cref="WhatsAppConnectionCardStatus"/> de una fila sin duplicar los resolvers que
/// ya existían: reusa <see cref="WhatsAppEmbeddedPendingConnection.StatusLabel"/> como fuente del
/// texto de onboarding, y el bloqueo Meta (131031) sigue viniendo de
/// <see cref="WhatsAppOutboundErrorClassifier"/> vía una consulta en lote (nunca N+1 por fila).
/// </summary>
public static class WhatsAppConnectionCardStatusExtensions
{
    /// <summary>
    /// Para un número YA operativo (ConversacionWhatsAppNumeroDto). Nunca puede caer en
    /// ActionRequired/Failed/Configuring -- esos son estados de onboarding, exclusivos de
    /// <see cref="ForPending"/>: un número operativo real ya superó el onboarding.
    /// </summary>
    public static WhatsAppConnectionCardStatus ForNumero(bool isBlocked, int usuariosCount)
        => isBlocked ? WhatsAppConnectionCardStatus.Blocked
         : usuariosCount <= 0 ? WhatsAppConnectionCardStatus.ReadyWithNoUsers
         : WhatsAppConnectionCardStatus.Ready;

    /// <summary>Para una conexión pendiente/fallida de Embedded Signup (todavía no es un número real).</summary>
    public static WhatsAppConnectionCardStatus ForPending(WhatsAppEmbeddedOnboardingStatus status) => status switch
    {
        WhatsAppEmbeddedOnboardingStatus.ActionRequired => WhatsAppConnectionCardStatus.ActionRequired,
        WhatsAppEmbeddedOnboardingStatus.FailedRetryable
            or WhatsAppEmbeddedOnboardingStatus.FailedFinal
            or WhatsAppEmbeddedOnboardingStatus.Expired
            or WhatsAppEmbeddedOnboardingStatus.Cancelled => WhatsAppConnectionCardStatus.Failed,
        _ => WhatsAppConnectionCardStatus.Configuring,
    };

    public static string Label(this WhatsAppConnectionCardStatus status) => status switch
    {
        WhatsAppConnectionCardStatus.Blocked => "Cuenta bloqueada",
        WhatsAppConnectionCardStatus.ActionRequired => "Acción requerida",
        WhatsAppConnectionCardStatus.Failed => "No pudimos completar la activación",
        WhatsAppConnectionCardStatus.Configuring => "Configurando",
        WhatsAppConnectionCardStatus.ReadyWithNoUsers => "Sin usuarios asignados",
        WhatsAppConnectionCardStatus.Ready => "Conectado",
        _ => "Conectado",
    };

    public static AlfaTagTone Tone(this WhatsAppConnectionCardStatus status) => status switch
    {
        WhatsAppConnectionCardStatus.Blocked => AlfaTagTone.Danger,
        WhatsAppConnectionCardStatus.ActionRequired => AlfaTagTone.Warning,
        WhatsAppConnectionCardStatus.Failed => AlfaTagTone.Warning,
        WhatsAppConnectionCardStatus.Configuring => AlfaTagTone.Accent,
        WhatsAppConnectionCardStatus.ReadyWithNoUsers => AlfaTagTone.Neutral,
        _ => AlfaTagTone.Success,
    };
}
