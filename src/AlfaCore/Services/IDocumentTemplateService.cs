using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IDocumentTemplateService
{
    Task<IReadOnlyList<DocumentTemplateDto>> GetListAsync(string tipoDocumento, string? uNegocio, CancellationToken ct = default);
    Task<IReadOnlyList<UnidadNegocioOptionDto>> GetUnidadesNegocioAsync(CancellationToken ct = default);
    Task<bool> CanEditSystemTemplatesAsync(CancellationToken ct = default);
    Task<DocumentTemplateDto?> GetByIdAsync(int idTemplate, CancellationToken ct = default);
    Task<DocumentTemplateDto> ResolveAsync(string tipoDocumento, string? uNegocio, CancellationToken ct = default);
    Task<DocumentTemplateDto> SaveAsync(DocumentTemplateSaveRequest request, CancellationToken ct = default);
    Task<DocumentTemplateDto> DuplicateAsync(int idTemplate, string? uNegocio, string? usuario, CancellationToken ct = default);
    DocumentTemplateDefinition DeserializeAndValidate(string templateJson);

    /// <summary>Imagen de portada de ESTA plantilla puntual (CORE_DocumentTemplate.PortadaImagen) --
    /// ya no es una imagen global de Cotizaciones, cada plantilla (por unidad de negocio) tiene la suya.</summary>
    Task<byte[]?> GetPortadaImageBytesAsync(int idTemplate, CancellationToken ct = default);
    Task SavePortadaImageAsync(int idTemplate, byte[] contenido, CancellationToken ct = default);
    Task DeletePortadaImageAsync(int idTemplate, CancellationToken ct = default);

    /// <summary>Tema visual (paleta de colores) aplicado a TODOS los documentos de esta base,
    /// independiente de la plantilla elegida en cada una.</summary>
    Task<string> GetGeneralThemeAsync(CancellationToken ct = default);
    Task SetGeneralThemeAsync(string themeKey, CancellationToken ct = default);
}
