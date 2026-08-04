using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaTurnoValidator
{
    Task<ValidationResult> ValidateAperturaAsync(PuntoVentaTurnoAperturaRequest request, CancellationToken ct = default);
    Task<ValidationResult> ValidateCierreAsync(PuntoVentaTurnoCierreRequest request, PuntoVentaTurnoDto turno, CancellationToken ct = default);
    Task<ValidationResult> ValidateCorteXAsync(PuntoVentaTurnoCorteXRequest request, PuntoVentaTurnoDto turno, CancellationToken ct = default);
}
