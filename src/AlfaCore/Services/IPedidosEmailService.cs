using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPedidosEmailService
{
    Task<CatalogoPedidoEmailResultDto> EnviarConfirmacionPedidoAsync(CatalogoPedidoEmailRequestDto request, CancellationToken ct = default);
}
