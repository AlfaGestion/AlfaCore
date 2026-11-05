namespace AlfaCore.Models;

public sealed record ConversacionWhatsAppVentanaDto(
    bool Activa,
    DateTime? Vencimiento = null,
    bool RechazadaPorMeta = false);
