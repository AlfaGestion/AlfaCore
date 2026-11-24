using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Punto de entrada para registrar señas sobre NP.
/// La escritura permanece cerrada hasta completar la validación financiera del circuito CBCT legacy.
/// </summary>
public sealed class PuntoVentaAnticipoRegistroService : IPuntoVentaAnticipoRegistroService
{
    public bool RegistroHabilitado => false;

    public Task<PuntoVentaAnticipoRegistroResultado> RegistrarAsync(
        PuntoVentaAnticipoRegistroSolicitud solicitud,
        CancellationToken ct = default)
    {
        PuntoVentaAnticipoRegistroValidaciones.ValidarSolicitud(solicitud);
        ct.ThrowIfCancellationRequested();

        var validation = new ValidationResult();
        validation.Add("POS_ANTICIPO_REGISTRO_NO_HABILITADO",
            "El registro de señas está temporalmente deshabilitado hasta validar el circuito contable CBCT en homologación.");
        return Task.FromException<PuntoVentaAnticipoRegistroResultado>(new AppValidationException(
            "El registro de señas está temporalmente deshabilitado hasta validar el circuito contable CBCT en homologación.",
            validation));
    }
}
