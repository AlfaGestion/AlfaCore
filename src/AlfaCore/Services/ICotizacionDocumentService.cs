using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICotizacionDocumentService
{
    Task<DocumentRenderResult> RenderAsync(long idVersion, string? uNegocio, CancellationToken ct = default);
    Task<DocumentRenderResult> RenderAsync(long idVersion, string? uNegocio, string tipoDocumento, CancellationToken ct = default);
    Task<byte[]> GeneratePdfAsync(long idVersion, string? uNegocio, CancellationToken ct = default);
    Task<byte[]> GeneratePdfAsync(long idVersion, string? uNegocio, string tipoDocumento, CancellationToken ct = default);
    Task<IReadOnlyList<NotaPedidoResumenDto>> SearchNotasPedidoRecientesAsync(int top = 20, CancellationToken ct = default);
    Task<DocumentRenderResult> RenderNotaPedidoAsync(int idComprobante, string? uNegocio, CancellationToken ct = default);
}
