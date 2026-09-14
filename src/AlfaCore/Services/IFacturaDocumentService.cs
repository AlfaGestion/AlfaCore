using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IFacturaDocumentService
{
    /// <summary>tc/idComprobante identifican el comprobante igual que en el visor interno
    /// (/comprobantes/viewer/{Tc}/{IdComprobante}) -- IdComprobante es el texto "SSSSNNNNNNNNL"
    /// (V_MV_Cpte.IDCOMPROBANTE), no el ID numérico interno.</summary>
    Task<DocumentRenderResult> RenderAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null);
    Task<byte[]> GeneratePdfAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null);

    /// <summary>Para Portal Cliente: resuelve el comprobante a partir del ID interno
    /// (V_MV_Cpte.ID) y valida que pertenezca a codigoCliente antes de generar el PDF -- nunca
    /// expone el comprobante de otra cuenta. Devuelve null si no existe, no pertenece al cliente,
    /// o no es una factura o nota de crédito/débito A/B/C.</summary>
    Task<byte[]?> GeneratePdfParaClienteAsync(string codigoCliente, int idComprobante, CancellationToken ct = default);

    /// <summary>Vista previa con la plantilla guardada de la unidad del comprobante.
    /// Valida pertenencia igual que el PDF; devuelve null si no existe, es ajeno o no es fiscal soportado.</summary>
    Task<DocumentRenderResult?> RenderParaClienteAsync(string codigoCliente, int idComprobante, CancellationToken ct = default);

    /// <summary>Últimos comprobantes reales de un tipo fiscal, para poblar el combo de preview del
    /// Diseñador -- mismo propósito que el combo de cotizaciones que ya usa esa pantalla.</summary>
    Task<IReadOnlyList<FacturaResumenDto>> SearchRecientesAsync(string tipoDocumento, int top = 20, CancellationToken ct = default);
}
