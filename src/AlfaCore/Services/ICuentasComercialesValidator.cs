using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICuentasComercialesValidator
{
    Task<ValidationResult> ValidateForSaveAsync(CuentaComercialTipo tipo, CuentaComercialSaveRequest request, CancellationToken ct = default);
}
