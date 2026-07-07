using Microsoft.AspNetCore.Components;

namespace AlfaCore.Services;

public sealed class PageHeaderNavigationService : IPageHeaderNavigationService
{
    private readonly List<string> _history = [];
    private int _index = -1;
    private bool _navigatingHistory;

    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _history.Count - 1;

    public void Record(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return;

        if (_navigatingHistory)
        {
            _navigatingHistory = false;
            if (_index >= 0 && _index < _history.Count && string.Equals(_history[_index], uri, StringComparison.Ordinal))
                return;
        }

        if (_index >= 0 && _index < _history.Count && string.Equals(_history[_index], uri, StringComparison.Ordinal))
            return;

        if (_index < _history.Count - 1)
            _history.RemoveRange(_index + 1, _history.Count - _index - 1);

        _history.Add(uri);
        _index = _history.Count - 1;
    }

    public void GoBack(NavigationManager navigation)
    {
        if (!CanGoBack)
            return;

        _index--;
        _navigatingHistory = true;
        navigation.NavigateTo(_history[_index]);
    }

    public void GoForward(NavigationManager navigation)
    {
        if (!CanGoForward)
            return;

        _index++;
        _navigatingHistory = true;
        navigation.NavigateTo(_history[_index]);
    }
}
