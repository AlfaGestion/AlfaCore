using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICargaComprobantesService
{
    Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarClientesDirectaAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarFacturasAsociadasAsync(string cliente, string unidad, string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCotizacionArticuloDto>> BuscarArticulosDirectaAsync(string cliente, string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCotizacionArticuloDto>> BuscarArticulosDirectaAsync(CargaComprobantesDirecta directa, string texto, CancellationToken ct = default)
        => BuscarArticulosDirectaAsync(directa.Cliente, texto, ct);
    /// <summary>Stock actual (V_MV_STOCK, sin anulados) por artículo; del depósito indicado o de todos.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetStockArticulosAsync(IReadOnlyList<string> articulos, string? deposito, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>());
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
    /// <summary>Clientes en condiciones de facturación automática a la fecha del filtro (solo consulta).</summary>
    Task<IReadOnlyList<CargaComprobantesFacturacionAutomaticaFila>> BuscarFacturacionAutomaticaAsync(CargaComprobantesFiltro filtro, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CargaComprobantesFacturacionAutomaticaFila>>([]);
    /// <summary>Arma y valida la factura automática de un cliente (ensayo revertido): no pide CAE ni graba.</summary>
    Task<CargaComprobantesFacturacionAutomaticaResultado> PrevisualizarFacturacionAutomaticaAsync(string cuenta, DateTime fechaFacturacion, CancellationToken ct = default)
        => throw new NotSupportedException();
    /// <summary>Datos del cliente para la carga directa: documento, IVA, domicilio, vendedor, condición, lista y clase.</summary>
    Task<CargaComprobantesClienteResumen?> GetClienteDirectaAsync(string cliente, CancellationToken ct = default)
        => Task.FromResult<CargaComprobantesClienteResumen?>(null);
    /// <summary>Visor de comprobantes de ventas: búsqueda paginada con filtros (respeta la unidad del usuario).</summary>
    Task<ComprobantesVisorResultado> BuscarComprobantesAsync(ComprobantesVisorFiltro filtro, CancellationToken ct = default)
        => throw new NotSupportedException();
    /// <summary>PDF para ver o reimprimir un comprobante ya grabado.</summary>
    Task<byte[]> GetPdfComprobanteAsync(string tc, string idComprobante, CancellationToken ct = default)
        => throw new NotSupportedException();
    /// <summary>PDF de cómo saldría impresa la factura automática de un cliente, con marca de vista previa. No graba nada.</summary>
    Task<byte[]> PrevisualizarPdfFacturacionAutomaticaAsync(string cuenta, DateTime fechaFacturacion, CancellationToken ct = default)
        => throw new NotSupportedException();
    /// <summary>Emite la factura automática de un cliente (homologación) y actualiza su última factura y cuotas.</summary>
    Task<CargaComprobantesFacturacionAutomaticaResultado> EmitirFacturacionAutomaticaAsync(string cuenta, DateTime fechaFacturacion, CancellationToken ct = default)
        => throw new NotSupportedException();
    Task<PagedResult<CargaComprobantesOrigen>> BuscarOrigenesAsync(CargaComprobantesFiltro filtro, CancellationToken ct = default);
    Task<CargaComprobantesOrigen> GetOrigenAsync(int id, CancellationToken ct = default);
    Task<CargaComprobantesSimulacion> SimularAsync(int origenId, int talonario, IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default);
}
