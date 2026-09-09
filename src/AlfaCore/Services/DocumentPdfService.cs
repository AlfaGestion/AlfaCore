using Microsoft.Playwright;

namespace AlfaCore.Services;

public sealed class DocumentPdfService(IAppEventService appEvents, ILogger<DocumentPdfService> logger) : IDocumentPdfService, IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<byte[]> GenerateAsync(string html, CancellationToken ct = default)
    {
        try
        {
            var browser = await GetBrowserAsync(ct);
            var page = await browser.NewPageAsync();
            try
            {
                await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
                var pdf = await page.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true, PreferCSSPageSize = true });
                logger.LogInformation("Documentos: PDF Playwright generado ({Bytes} bytes).", pdf.Length);
                return pdf;
            }
            finally { await page.CloseAsync(); }
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync("Documentos", "GenerarPdfPlaywright", ex, "No se pudo generar el PDF beta con Chromium.", ct: ct);
            throw new AppUserFacingException("No se pudo generar el PDF beta. Verificá la instalación de Chromium en el servidor.", incidentId, ex);
        }
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is not null) return _browser;
        await _gate.WaitAsync(ct);
        try
        {
            if (_browser is not null) return _browser;
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return _browser;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _gate.Dispose();
    }
}
