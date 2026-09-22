using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

public interface ITerminalsService
{
    Task<IReadOnlyList<TerminalInfo>> ListTerminalInfosAsync(MercadoPagoPointOptions options, CancellationToken ct);
}
