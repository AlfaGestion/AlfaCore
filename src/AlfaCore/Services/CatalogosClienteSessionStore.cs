using AlfaCore.Models;
using System.Collections.Concurrent;

namespace AlfaCore.Services;

public sealed class CatalogosClienteSessionStore
{
    private readonly ConcurrentDictionary<string, CatalogosClienteSessionInfo> _store = new();

    public string Store(CatalogosClienteSessionInfo info)
    {
        var token = Guid.NewGuid().ToString("N");
        _store[token] = info;
        return token;
    }

    public void Upsert(string token, CatalogosClienteSessionInfo info)
        => _store[token] = info;

    public bool TryGet(string token, out CatalogosClienteSessionInfo? info)
        => _store.TryGetValue(token, out info);

    public void Remove(string token)
        => _store.TryRemove(token, out _);
}
