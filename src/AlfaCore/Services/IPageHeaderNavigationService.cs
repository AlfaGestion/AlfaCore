using Microsoft.AspNetCore.Components;

namespace AlfaCore.Services;

public interface IPageHeaderNavigationService
{
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    void Record(string uri);
    void GoBack(NavigationManager navigation);
    void GoForward(NavigationManager navigation);
}
