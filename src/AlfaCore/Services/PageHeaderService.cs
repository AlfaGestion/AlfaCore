using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class PageHeaderService : IPageHeaderService
{
    public event Action? Changed;

    public PageHeaderConfig Current { get; private set; } = PageHeaderConfig.Empty;

    public void Set(PageHeaderConfig config)
    {
        Current = config;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = PageHeaderConfig.Empty;
        Changed?.Invoke();
    }
}
