using Microsoft.Playwright;

namespace AlfaCore.Services;

// Registrado como Singleton (ver Program.cs): lanzar un Chromium headless por request sería
// carísimo (~1-2s + memoria) cuando el propio diseño de la clase (el _gate + "_browser is not
// null" de GetBrowserAsync) ya asume una única instancia reutilizada. Por eso NO recibe
// IAppEventService por constructor (sería una dependencia "cautiva": un scoped capturado para
// siempre por un singleton) -- resuelve un scope propio recién cuando necesita loguear un error.
public sealed class DocumentPdfService(IServiceScopeFactory scopeFactory, ILogger<DocumentPdfService> logger) : IDocumentPdfService, IAsyncDisposable
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
            await using var scope = scopeFactory.CreateAsyncScope();
            var appEvents = scope.ServiceProvider.GetRequiredService<IAppEventService>();
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
