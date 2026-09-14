using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICotizacionDocumentService
{
    Task<DocumentRenderResult> RenderAsync(long idVersion, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null);
    Task<byte[]> GeneratePdfAsync(long idVersion, string? uNegocio, CancellationToken ct = default, DocumentTemplateDto? previewTemplate = null);
}
