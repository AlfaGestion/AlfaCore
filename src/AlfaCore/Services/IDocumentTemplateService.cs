using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IDocumentTemplateService
{
    Task<IReadOnlyList<DocumentTemplateDto>> GetListAsync(string tipoDocumento, string? uNegocio, CancellationToken ct = default);
    Task<IReadOnlyList<UnidadNegocioOptionDto>> GetUnidadesNegocioAsync(CancellationToken ct = default);
    Task<DocumentTemplateDto?> GetByIdAsync(int idTemplate, CancellationToken ct = default);
    Task<DocumentTemplateDto> ResolveAsync(string tipoDocumento, string? uNegocio, CancellationToken ct = default);
    Task<DocumentTemplateDto> SaveAsync(DocumentTemplateSaveRequest request, CancellationToken ct = default);
    Task<DocumentTemplateDto> DuplicateAsync(int idTemplate, string? uNegocio, string? usuario, CancellationToken ct = default);
    DocumentTemplateDefinition DeserializeAndValidate(string templateJson);
}
