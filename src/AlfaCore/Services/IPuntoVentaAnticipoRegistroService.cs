using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaAnticipoRegistroService
{
    bool RegistroHabilitado { get; }
    Task<PuntoVentaAnticipoRegistroResultado> RegistrarAsync(
        PuntoVentaAnticipoRegistroSolicitud solicitud,
        CancellationToken ct = default);
}
