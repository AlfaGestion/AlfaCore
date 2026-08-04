using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaTurnoService
{
    Task<IReadOnlyList<PuntoVentaDenominacionDto>> GetDenominacionesAsync(CancellationToken ct = default);
    Task<PuntoVentaTurnoDto?> GetTurnoAbiertoAsync(int idPuntoVenta, string idCaja, CancellationToken ct = default);
    Task<PuntoVentaTurnoDto?> GetTurnoByIdAsync(int id, CancellationToken ct = default);
    Task<int> AbrirTurnoAsync(PuntoVentaTurnoAperturaRequest request, CancellationToken ct = default);
    Task CerrarTurnoAsync(PuntoVentaTurnoCierreRequest request, CancellationToken ct = default);
    Task<decimal> GetSaldoTeoricoActualAsync(int idTurno, CancellationToken ct = default);
    Task RegistrarMovimientoAsync(int idTurno, string tc, string idComprobante, decimal importeTotal, decimal importeEfectivo, CancellationToken ct = default);
    Task RegistrarCorteXAsync(PuntoVentaTurnoCorteXRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaTurnoCorteXDto>> GetCortesXAsync(int idTurno, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaCajaSaldoDto>> GetSaldosPorCajaAsync(int idPuntoVenta, CancellationToken ct = default);
}
