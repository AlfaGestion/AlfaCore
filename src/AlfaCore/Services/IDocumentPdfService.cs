namespace AlfaCore.Services;

public interface IDocumentPdfService
{
    Task<byte[]> GenerateAsync(string html, CancellationToken ct = default);
}
