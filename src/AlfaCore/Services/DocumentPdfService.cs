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

    public async Task<string> PrepareHtmlAsync(string html, CancellationToken ct = default)
    {
        try
        {
            var browser = await GetBrowserAsync(ct);
            var page = await browser.NewPageAsync();
            try
            {
                await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
                await page.EvaluateAsync("async () => { if (window.alfaDocumentReady) await window.alfaDocumentReady; }");
                await PrepareTicketAsync(page);
                // La vista previa recibe páginas estáticas ya medidas. Los errores del paginador
                // pasan por AUX_ERR, igual que la exportación, y no quedan solo en el navegador.
                await page.EvaluateAsync("() => document.querySelectorAll('script').forEach(s => s.remove())");
                return await page.ContentAsync();
            }
            finally { await page.CloseAsync(); }
        }
        catch (Exception ex)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var appEvents = scope.ServiceProvider.GetRequiredService<IAppEventService>();
            if (EsChromiumFaltante(ex))
                throw await AvisoChromiumFaltanteAsync(appEvents, ex, ct);
            var incidentId = await appEvents.LogErrorAsync("Documentos", "PaginarDocumento", ex,
                "No se pudo acomodar el cierre del documento en la última página.", ct: ct);
            throw new AppUserFacingException("No se pudo paginar el documento. Revisá el tamaño de los bloques o desactivá Totales al pie de página.", incidentId, ex);
        }
    }

    public async Task<byte[]> GenerateAsync(string html, DocumentPdfFooterOptions? footer = null, CancellationToken ct = default)
    {
        try
        {
            var browser = await GetBrowserAsync(ct);
            var page = await browser.NewPageAsync();
            try
            {
                await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
                await page.EvaluateAsync("async () => { if (window.alfaDocumentReady) await window.alfaDocumentReady; }");
                var ticket = await PrepareTicketAsync(page);
                // El margen real (arriba/abajo/lados, incluido el espacio para el pie) lo define el
                // CSS @page con nombre en DocumentRenderer -- no el Margin de Playwright, que con
                // PreferCSSPageSize=true queda ignorado para el layout del contenido y solo termina
                // confundiendo dónde cree Chromium que va el pie.
                var options = new PagePdfOptions { Format = "A4", PrintBackground = true, PreferCSSPageSize = true };
                if (!ticket && footer is { } f && (f.ShowPageNumber || f.ShowCompanyName))
                {
                    options.DisplayHeaderFooter = true;
                    options.HeaderTemplate = "<span></span>";
                    options.FooterTemplate = BuildFooterTemplate(f);
                }
                var pdf = await page.PdfAsync(options);
                logger.LogInformation("Documentos: PDF Playwright generado ({Bytes} bytes).", pdf.Length);
                return pdf;
            }
            finally { await page.CloseAsync(); }
        }
        catch (Exception ex)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var appEvents = scope.ServiceProvider.GetRequiredService<IAppEventService>();
            if (EsChromiumFaltante(ex))
                throw await AvisoChromiumFaltanteAsync(appEvents, ex, ct);
            var incidentId = await appEvents.LogErrorAsync("Documentos", "GenerarPdfPlaywright", ex, "No se pudo generar el PDF beta con Chromium.", ct: ct);
            throw new AppUserFacingException("No se pudo generar el PDF beta. Verificá la instalación de Chromium en el servidor.", incidentId, ex);
        }
    }

    // ── Chromium faltante: aviso claro e instalación automática ──────────────────────────────────
    // Playwright usa un Chromium propio que se descarga aparte (la carpeta ms-playwright del perfil del
    // usuario que corre AlfaCore). En un servidor nuevo falta y todos los PDF fallaban con un mensaje
    // técnico. Ahora se instala solo una vez en segundo plano y el usuario recibe qué hacer.
    private static readonly object InstalacionLock = new();
    private static Task<int>? _instalacionChromium;

    internal static bool EsChromiumFaltante(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PlaywrightException && (e.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("playwright.ps1 install", StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    private Task<int> IniciarInstalacionChromium()
    {
        lock (InstalacionLock)
        {
            // Una sola descarga a la vez; si la anterior falló, se reintenta en el próximo pedido.
            if (_instalacionChromium is { IsCompleted: false } enCurso) return enCurso;
            if (_instalacionChromium is { IsCompletedSuccessfully: true, Result: 0 } lista) return lista;
            _instalacionChromium = Task.Run(() =>
            {
                logger.LogWarning("Documentos: falta Chromium de Playwright; se inicia la instalación automática.");
                try
                {
                    var codigo = Microsoft.Playwright.Program.Main(["install", "chromium"]);
                    logger.LogWarning("Documentos: instalación de Chromium terminada con código {Codigo}.", codigo);
                    return codigo;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Documentos: no se pudo instalar Chromium automáticamente.");
                    return -1;
                }
            });
            return _instalacionChromium;
        }
    }

    private async Task<AppUserFacingException> AvisoChromiumFaltanteAsync(IAppEventService appEvents, Exception ex, CancellationToken ct)
    {
        var instalacion = IniciarInstalacionChromium();
        // Unos segundos de margen: si la descarga es rápida, el usuario ya puede reintentar.
        await Task.WhenAny(instalacion, Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None));
        string mensaje;
        if (instalacion.IsCompletedSuccessfully && instalacion.Result == 0)
            mensaje = "El componente para generar PDF (Chromium) se acaba de instalar en el servidor. Volvé a intentar.";
        else if (!instalacion.IsCompleted)
            mensaje = "Falta el componente para generar PDF (Chromium) en el servidor y se está instalando automáticamente. " +
                      "Volvé a intentar en uno o dos minutos.";
        else
            mensaje = "Falta el componente para generar PDF (Chromium) en el servidor y no se pudo instalar solo (por ejemplo, sin acceso a internet). " +
                      "Pedile al administrador que, en la carpeta de AlfaCore del servidor, ejecute: pwsh playwright.ps1 install chromium";
        var incidentId = await appEvents.LogErrorAsync("Documentos", "ChromiumFaltante", ex, mensaje, ct: ct);
        return new AppUserFacingException(mensaje, incidentId, ex);
    }

    private static Task<bool> PrepareTicketAsync(IPage page) => page.EvaluateAsync<bool>("""
        async () => {
            if (!document.querySelector('meta[name="alfa-paper"][content="Ticket80"]')) return false;
            await document.fonts.ready;
            await Promise.all(Array.from(document.images, image => image.decode().catch(() => {})));
            const height = Math.ceil(document.querySelector('main.doc').getBoundingClientRect().height * 25.4 / 96) + 1;
            if (height > 5000) throw new Error('El ticket supera el largo máximo de 5 metros. Utilizá A4.');
            let style = document.getElementById('ticket-page-size');
            if (!style) { style = document.createElement('style'); style.id = 'ticket-page-size'; document.head.append(style); }
            style.textContent = '@page ticket{size:80mm ' + Math.max(20, height) + 'mm;margin:0}';
            return true;
        }
        """);

    private static string BuildFooterTemplate(DocumentPdfFooterOptions f)
    {
        var empresa = f.ShowCompanyName && !string.IsNullOrWhiteSpace(f.CompanyName)
            ? System.Net.WebUtility.HtmlEncode(f.CompanyName)
            : string.Empty;
        var paginado = f.ShowPageNumber
            ? "Página <span class=\"pageNumber\"></span> de <span class=\"totalPages\"></span>"
            : string.Empty;
        return "<div style=\"width:100%;font-size:8px;color:#667;padding:0 10mm;display:flex;justify-content:space-between;align-items:center;\">"
            + $"<span>{empresa}</span><span>{paginado}</span></div>";
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is not null) return _browser;
        await _gate.WaitAsync(ct);
        try
        {
            if (_browser is not null) return _browser;
            _playwright ??= await Playwright.CreateAsync();
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
