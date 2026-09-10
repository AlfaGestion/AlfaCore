namespace AlfaCore.Services;

/// <summary>Pie de página opcional (número de página / nombre de empresa), generado con el
/// mecanismo nativo de Playwright (DisplayHeaderFooter), no con CSS @page/counter -- es el único
/// que Chromium respeta de forma confiable para numeración real de página.</summary>
public sealed record DocumentPdfFooterOptions(bool ShowPageNumber, bool ShowCompanyName, string? CompanyName);

public interface IDocumentPdfService
{
    Task<byte[]> GenerateAsync(string html, DocumentPdfFooterOptions? footer = null, CancellationToken ct = default);
}
