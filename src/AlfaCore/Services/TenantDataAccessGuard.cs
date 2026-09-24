namespace AlfaCore.Services;

/// <summary>
/// Regla única para que el shell (MainLayout) ejecute consultas tenant-sensitive FUERA del
/// <c>@Body</c> (polling de notificaciones de Conversaciones, panel rápido de tareas, etc.).
/// Es la misma fuente de verdad que decide montar la página (MainLayout.IsActiveSessionAuthorized
/// / <see cref="TenantBodyGate"/>): usuario autorizado para la sesión activa según
/// <see cref="IAppUserSessionService.IsAuthorizedForSession"/>.
/// Antes el polling y el panel sólo miraban IsAuthenticated: un usuario autenticado de la empresa
/// A que abría /{idweb de B}/{idbase de B} veía, aun con la página bloqueada, nombre de contacto y
/// resumen del último mensaje de B (o de ConnectionStrings:AlfaGestion con sesión null).
/// </summary>
public static class TenantDataAccessGuard
{
    /// <summary>
    /// Sesión activa autorizada para el usuario actual. False si no hay sesión (ruta root, ruta
    /// rechazada), si no hay usuario o si el usuario no pasó el login de ESA base (incluye la
    /// transición de base: SwitchSession invalida la autorización anterior).
    /// </summary>
    public static bool TryGetAuthorizedSession(ISessionService sessionService, IAppUserSessionService appUserSession, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        if (!appUserSession.IsAuthenticated)
            return false;

        var active = sessionService.GetActiveSession();
        if (active is null || !appUserSession.IsAuthorizedForSession(active.Id))
            return false;

        sessionId = active.Id;
        return true;
    }

    public static bool IsActiveSessionAuthorized(ISessionService sessionService, IAppUserSessionService appUserSession)
        => TryGetAuthorizedSession(sessionService, appUserSession, out _);

    /// <summary>
    /// Ejecuta <paramref name="query"/> sólo si hay sesión autorizada, y descarta el resultado si al
    /// terminar la sesión autorizada ya no es la misma (cambio de base / logout a mitad de la
    /// consulta): nunca se muestra en la base B algo leído mientras la activa era A.
    /// </summary>
    public static async Task<TenantScopedResult<T>> RunForAuthorizedSessionAsync<T>(
        ISessionService sessionService,
        IAppUserSessionService appUserSession,
        Func<CancellationToken, Task<T>> query,
        CancellationToken ct = default)
    {
        if (!TryGetAuthorizedSession(sessionService, appUserSession, out var before))
            return TenantScopedResult<T>.Skipped;

        var value = await query(ct).ConfigureAwait(false);

        if (!TryGetAuthorizedSession(sessionService, appUserSession, out var after) || after != before)
            return TenantScopedResult<T>.Skipped;

        return new TenantScopedResult<T>(true, before, value);
    }
}

public readonly record struct TenantScopedResult<T>(bool Executed, Guid SessionId, T? Value)
{
    public static TenantScopedResult<T> Skipped => new(false, Guid.Empty, default);
}
