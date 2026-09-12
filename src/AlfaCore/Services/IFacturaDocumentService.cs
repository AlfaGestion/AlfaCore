using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IFacturaDocumentService
{
    /// <summary>tc/idComprobante identifican el comprobante igual que en el visor interno
    /// (/comprobantes/viewer/{Tc}/{IdComprobante}) -- IdComprobante es el texto "SSSSNNNNNNNNL"
    /// (V_MV_Cpte.IDCOMPROBANTE), no el ID numérico interno.</summary>
    Task<DocumentRenderResult> RenderAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default);
    Task<byte[]> GeneratePdfAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default);

    /// <summary>Para Portal Cliente: resuelve el comprobante a partir del ID interno
    /// (V_MV_Cpte.ID) y valida que pertenezca a codigoCliente antes de generar el PDF -- nunca
    /// expone el comprobante de otra cuenta. Devuelve null si no existe, no pertenece al cliente,
    /// o no es una Factura A/B/C.</summary>
    Task<byte[]?> GeneratePdfParaClienteAsync(string codigoCliente, int idComprobante, CancellationToken ct = default);

    /// <summary>Últimos comprobantes reales de una letra, para poblar el combo de preview del
    /// Diseñador -- mismo propósito que el combo de cotizaciones que ya usa esa pantalla.</summary>
    Task<IReadOnlyList<FacturaResumenDto>> SearchRecientesAsync(string letra, int top = 20, CancellationToken ct = default);
}
