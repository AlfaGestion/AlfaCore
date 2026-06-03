using AlfaCore.Models;
using System.Collections.Concurrent;

namespace AlfaCore.Services;

public sealed class Vb6BridgeTicketStore
{
    private readonly ConcurrentDictionary<string, Vb6BridgeTicketRecord> _store = new();

    public string Create(Vb6BridgeTicketRecord record)
    {
        var ticket = Guid.NewGuid().ToString("N");
        _store[ticket] = record with { Ticket = ticket };
        return ticket;
    }

    public bool TryConsume(string ticket, out Vb6BridgeTicketRecord? record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(ticket))
            return false;

        if (!_store.TryRemove(ticket.Trim(), out var existing))
            return false;

        if (existing.ExpiresAt <= DateTime.UtcNow)
            return false;

        record = existing;
        return true;
    }
}
