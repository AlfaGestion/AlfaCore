using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IAppUserSessionService
{
    event Action? StateChanged;

    bool IsAuthenticated { get; }
    AppUserSessionInfo? CurrentUser { get; }
    bool RequiresInternalLogin { get; }

    string? CurrentToken { get; }

    Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default);
    void AdoptInternalUser(AppUserSessionInfo internalUser);
    bool TryRestoreFromToken(string token);
    void Logout();
    void HandleSqlSessionChanged();
    string GetCurrentUserName(string fallback = "");

    /// <summary>
    /// True si el usuario actual completó el login interno (TA_USUARIOS) contra la base indicada
    /// por <paramref name="activeSessionId"/>. Nunca confiar en <see cref="RequiresInternalLogin"/>
    /// solo: esa bandera no distingue para qué base se validó, por eso cambiar de base activa sin
    /// volver a autenticar debe reportar false acá.
    /// </summary>
    bool IsAuthorizedForSession(Guid? activeSessionId);

    /// <summary>
    /// Invalida el login interno vigente si la base activa (<paramref name="activeSessionId"/>) no
    /// es la misma contra la que se validó por última vez. Debe llamarse cada vez que cambia la
    /// base activa (ver <see cref="IConexionClienteService.SwitchSession"/>) para que un cambio de
    /// idbase — manual por URL o por el selector de bases — nunca herede la autorización de otra
    /// base sin repetir la validación de usuario/contraseña.
    /// </summary>
    void EnsureAuthorizedForSession(Guid? activeSessionId);
}
