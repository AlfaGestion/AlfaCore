using AlfaCore.Models;

namespace AlfaCore.Services;

// Identidad acotada al trabajo autorizado y a UNA sesión SQL. Nunca se adopta en el circuito web.
internal sealed class TarjetasUsuarioWorker(AppUserSessionInfo usuario,Guid sesion):IAppUserSessionService
{
    public event Action? StateChanged { add{} remove{} }
    public bool IsAuthenticated=>true;
    public AppUserSessionInfo CurrentUser=>usuario;
    public bool RequiresInternalLogin=>false;
    public string? CurrentToken=>null;
    public string GetCurrentUserName(string fallback="")=>usuario.UserName;
    public bool IsAuthorizedForSession(Guid? id)=>id==sesion;
    public void EnsureAuthorizedForSession(Guid? id){if(id!=sesion)throw new OperationCanceledException("La base del trabajo cambió.");}
    public Task<AppUserSessionInfo> LoginAsync(string u,string p,CancellationToken ct=default)=>throw new NotSupportedException();
    public void AdoptInternalUser(AppUserSessionInfo u)=>throw new NotSupportedException();
    public bool TryRestoreFromToken(string t)=>false;
    public void Logout()=>throw new NotSupportedException();
    public void HandleSqlSessionChanged()=>throw new NotSupportedException();
}
