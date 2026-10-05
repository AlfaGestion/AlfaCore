using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Consulta del polling global de notificaciones de Conversaciones (MainLayout). Corre fuera del
/// <c>@Body</c>, por eso pasa siempre por <see cref="TenantDataAccessGuard"/>: sin sesión activa
/// autorizada para el usuario (ruta root, ruta rechazada, base de otro tenant sin login interno,
/// cambio de base en curso) no se toca IConversacionesService.
/// </summary>
public sealed class GlobalConversationNotificationsSource(
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IConversacionesService conversacionesService)
{
    public Task<TenantScopedResult<IReadOnlyList<ConversacionInboxItemDto>>> FetchPendingAsync(CancellationToken ct = default)
        => TenantDataAccessGuard.RunForAuthorizedSessionAsync<IReadOnlyList<ConversacionInboxItemDto>>(
            sessionService,
            appUserSession,
            async token =>
            {
                if (!await conversacionesService.HasConversationSchemaAsync(token).ConfigureAwait(false))
                    return [];

                return await conversacionesService.GetInboxAsync(new ConversacionesInboxFilters
                {
                    Canal = "WHATSAPP",
                    Modo = "pendientes",
                    Limit = 10
                }, token).ConfigureAwait(false);
            },
            ct);

    /// <summary>
    /// Bandeja de la burbuja de la barra superior: conversaciones abiertas de todos los canales, las
    /// más recientes primero. Mismo control de sesión autorizada que <see cref="FetchPendingAsync"/>;
    /// la visibilidad por número/usuario la aplica GetInboxAsync como en la bandeja de Conversaciones.
    /// </summary>
    public Task<TenantScopedResult<IReadOnlyList<ConversacionInboxItemDto>>> FetchBandejaAsync(int limit = 30, CancellationToken ct = default)
        => TenantDataAccessGuard.RunForAuthorizedSessionAsync<IReadOnlyList<ConversacionInboxItemDto>>(
            sessionService,
            appUserSession,
            async token =>
            {
                if (!await conversacionesService.HasConversationSchemaAsync(token).ConfigureAwait(false))
                    return [];

                return await conversacionesService.GetInboxAsync(new ConversacionesInboxFilters
                {
                    Modo = "pendientes",
                    Limit = Math.Clamp(limit, 1, 50)
                }, token).ConfigureAwait(false);
            },
            ct);

    /// <summary>Separa la bandeja en "sin leer" (no leídos del usuario) y "sin responder" (último mensaje del cliente).</summary>
    public static (IReadOnlyList<ConversacionInboxItemDto> SinLeer, IReadOnlyList<ConversacionInboxItemDto> SinResponder) Clasificar(
        IEnumerable<ConversacionInboxItemDto> items)
    {
        var lista = items.Where(x => !x.Archivada && !x.Bloqueada).ToList();
        var sinLeer = lista
            .Where(x => x.MensajesNoLeidosUsuario > 0)
            .OrderByDescending(x => x.FechaHoraUltimoMensajeCliente ?? x.FechaHoraUltimoMensaje)
            .ToList();
        var sinResponder = lista
            .Where(x => string.Equals(x.DireccionUltimoMensaje, "ENTRANTE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.FechaHoraUltimoMensajeCliente ?? x.FechaHoraUltimoMensaje)
            .ToList();
        return (sinLeer, sinResponder);
    }
}
