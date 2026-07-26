using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaConfigValidator
{
    Task<ValidationResult> ValidatePuntoVentaForSaveAsync(PuntoVentaEntidadSaveRequest request, CancellationToken ct = default);
    Task<ValidationResult> ValidateSectorForSaveAsync(PuntoVentaSectorSaveRequest request, CancellationToken ct = default);
    Task<ValidationResult> ValidateMesaForSaveAsync(PuntoVentaMesaSaveRequest request, CancellationToken ct = default);
}
