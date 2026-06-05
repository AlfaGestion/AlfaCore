using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IVb6BridgeService
{
    Task<string> CreateTicketAsync(Vb6AuthTicketRequest request, CancellationToken ct = default);
    Task<Vb6ConsumeTicketResult> ConsumeTicketAsync(string ticket, CancellationToken ct = default);
}
