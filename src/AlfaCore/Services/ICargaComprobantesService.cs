using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICargaComprobantesService
{
    Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarClientesDirectaAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarFacturasAsociadasAsync(string cliente, string unidad, string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCotizacionArticuloDto>> BuscarArticulosDirectaAsync(string cliente, string texto, CancellationToken ct = default);
    Task<CargaComprobantesPlanGrabacion> ValidarDirectaAsync(CargaComprobantesDirecta directa, int talonario, CancellationToken ct = default);
    Task<CargaComprobantesGrabacionResultado?> ConsultarDirectaAsync(Guid borrador, Guid operacion, CancellationToken ct = default);
    Task<CargaComprobantesGrabacionResultado> GrabarDirectaAsync(CargaComprobantesDirecta directa, int talonario,
        Guid operacion, string huellaEsperada, CancellationToken ct = default);
    Task<CargaComprobantesContexto> GetContextoAsync(CancellationToken ct = default);
    Task<CargaComprobantesPlanGrabacion> ValidarGrabacionAsync(int origenId, int talonario,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default);
    Task<CargaComprobantesGrabacionResultado> GrabarFacturaAsync(int origenId, int talonario,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion, string huellaEsperada, CancellationToken ct = default);
    Task<CargaComprobantesGrabacionResultado> RecuperarGrabacionAsync(Guid operacion, CancellationToken ct = default);
    Task<CargaComprobantesGrabacionResultado?> GetUltimaGrabacionAsync(int origenId, CancellationToken ct = default);
    Task<IReadOnlyList<CargaComprobantesGrabacionResultado>> GetGrabacionesRecientesAsync(CancellationToken ct = default);
    Task<byte[]> GetPdfFacturaAsync(Guid operacion, CancellationToken ct = default);
    Task<CargaComprobantesElectronicaPrueba> RecuperarPruebaElectronicaAsync(Guid operacion, CancellationToken ct = default);
    Task<CargaComprobantesElectronicaEstado> VerificarElectronicaAsync(int? origenId = null, CancellationToken ct = default);
    Task<CargaComprobantesElectronicaPrueba?> GetUltimaPruebaElectronicaAsync(int origenId, CancellationToken ct = default);
    Task<CargaComprobantesElectronicaPrueba> EmitirPruebaElectronicaAsync(int origenId, int talonario,
        IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion, CancellationToken ct = default);
    Task<PagedResult<CargaComprobantesOrigen>> BuscarOrigenesAsync(CargaComprobantesFiltro filtro, CancellationToken ct = default);
    Task<CargaComprobantesOrigen> GetOrigenAsync(int id, CancellationToken ct = default);
    Task<CargaComprobantesSimulacion> SimularAsync(int origenId, int talonario, IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default);
}
