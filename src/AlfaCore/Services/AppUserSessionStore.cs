using AlfaCore.Models;
using System.Collections.Concurrent;

namespace AlfaCore.Services;

public sealed class AppUserSessionStore
{
    private readonly ConcurrentDictionary<string, AppUserSessionInfo> _store = new();

    public string Store(AppUserSessionInfo info)
    {
        var token = Guid.NewGuid().ToString("N");
        _store[token] = info;
        return token;
    }

    public void Upsert(string token, AppUserSessionInfo info)
        => _store[token] = info;

    public bool TryGet(string token, out AppUserSessionInfo? info)
        => _store.TryGetValue(token, out info);

    public void Remove(string token)
        => _store.TryRemove(token, out _);
}
