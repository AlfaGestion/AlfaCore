using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IComprobanteViewerService
{
    Task<ComprobanteViewerDto?> GetAsync(string tc, string idComprobante, int idComplemento = 0, CancellationToken ct = default);
    Task<ComprobanteDocumentoArchivoDto?> GetDocumentoArchivoAsync(string tc, string idComprobante, int idComplemento, string documento, CancellationToken ct = default);
}
