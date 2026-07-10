using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPageHeaderService
{
    event Action? Changed;
    PageHeaderConfig Current { get; }
    void Set(PageHeaderConfig config);
    void Clear();
}
