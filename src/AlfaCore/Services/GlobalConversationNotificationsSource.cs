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
}
