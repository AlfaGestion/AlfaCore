namespace AlfaCore.Services;

public sealed class AppModeService(IConfiguration configuration) : IAppModeService
{
    public bool IsSaaSMode { get; } = configuration.GetValue("ModoSaaS", false);
}
