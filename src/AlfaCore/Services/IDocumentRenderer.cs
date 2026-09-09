using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IDocumentRenderer
{
    string RenderCotizacion(DocumentTemplateDefinition template, CotizacionDocumentData data, string? cssCustom = null, string? themeKey = null);
}
