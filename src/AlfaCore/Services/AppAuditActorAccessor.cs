namespace AlfaCore.Services;

public interface IAppAuditActorAccessor
{
    string UserName { get; set; }
    string CentralLogin { get; set; }
    void Clear();
}

public sealed class AppAuditActorAccessor : IAppAuditActorAccessor
{
    public string UserName { get; set; } = string.Empty;
    public string CentralLogin { get; set; } = string.Empty;

    public void Clear()
    {
        UserName = string.Empty;
        CentralLogin = string.Empty;
    }
}
