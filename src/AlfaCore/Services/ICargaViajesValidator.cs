using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICargaViajesValidator
{
    Task<ValidationResult> ValidateViajeForSaveAsync(CargaViajeSaveRequest request, CancellationToken ct = default);
    Task<ValidationResult> ValidateTarifaForSaveAsync(CargaViajeTarifaSaveRequest request, CancellationToken ct = default);
    Task<ValidationResult> ValidateChoferForSaveAsync(CargaViajeChoferSaveRequest request, CancellationToken ct = default);
    Task<ValidationResult> ValidateDestinoForSaveAsync(CargaViajeDestinoSaveRequest request, CancellationToken ct = default);
}
