using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

public interface IPosService
{
    Task<IReadOnlyList<PosInfo>> ListPosInfosAsync(MercadoPagoPointOptions options, CancellationToken ct);
}
