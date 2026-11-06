using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Orden de pago (OPG) y pago proforma (PGFP) impresos con el Diseñador de comprobantes.</summary>
public interface IOrdenPagoDocumentService
{
    /// <summary>tc/idComprobante identifican la orden como en V_MV_Cpte (TC e IDCOMPROBANTE «SSSSNNNNNNNNL»). Sin plantilla de vista previa usa la guardada de la unidad.</summary>
    Task<DocumentRenderResult> RenderAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null);
    Task<byte[]> GeneratePdfAsync(string tc, string idComprobante, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null);
    /// <summary>Últimas órdenes reales del tipo (ORDEN_PAGO o PAGO_PROFORMA) para el combo de vista previa del Diseñador.</summary>
    Task<IReadOnlyList<FacturaResumenDto>> SearchRecientesAsync(string tipoDocumento, int top = 20, CancellationToken ct = default);
}
