using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaPedidoValidator
{
    Task<ValidationResult> ValidatePedidoForSaveAsync(PuntoVentaPedidoSaveRequest request, CancellationToken ct = default);
}
