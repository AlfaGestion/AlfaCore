using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlfaCore.Tests;

public sealed class DocumentPaginationTests
{
    [Theory]
    [InlineData(1, "A4", "Portrait", false)]
    [InlineData(70, "A4", "Portrait", false)]
    [InlineData(70, "A5", "Portrait", false)]
    [InlineData(70, "A4", "Landscape", false)]
    [InlineData(1, "A4", "Portrait", true)]
    public async Task CierreSoloEnUltimaPaginaConAlturaReal(int count, string size, string orientation, bool longRow)
    {
        var template = DocumentTemplateDefinition.CrearFacturaEstandar("A");
        template.TotalesAlPiePagina = true;
        template.Paper.Size = size;
        template.Paper.Orientation = orientation;
        template.Blocks.Single(b => b.Type == TiposBloqueDocumento.Pie).Visible = true;
        var data = new FacturaDocumentData
        {
            Empresa = new() { Nombre = "Empresa de prueba" },
            Comprobante = new() { TipoDocumento = TiposDocumentoCore.CreditoA, Letra = "A", CodigoAfip = "003", Numero = "00000001" },
            Cae = new() { Cae = "70417054367476", Resultado = "A", VencimientoCae = new DateTime(2026, 9, 24) },
            Items = Enumerable.Range(1, count).Select(i => new FacturaDocumentItemData
            {
                Codigo = i.ToString(), Descripcion = longRow ? string.Concat(Enumerable.Repeat("Descripción extensa del artículo. ", 700)) : $"Artículo de prueba número {i}",
                Cantidad = 1, PrecioUnitario = 121, Subtotal = 121
            }).ToList(),
            Totales = new() { NetoGravado = 100 * count, Total = 121 * count }
        };
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        var html = new DocumentRenderer().RenderFactura(template, data);
        await page.SetContentAsync(html);
        await page.EvaluateAsync("async () => await window.alfaDocumentReady");
        var pages = await page.Locator(".document-sheet").CountAsync();
        Assert.True(count == 1 && !longRow ? pages == 1 : pages > 1);
        Assert.Equal(1, await page.Locator(".document-closing").CountAsync());
        Assert.Equal(1, await page.Locator(".document-sheet:last-child .totals").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const last = document.querySelector('.document-sheet:last-child');
                const close = last.querySelector('.document-closing').getBoundingClientRect();
                const body = last.querySelector('.document-sheet-body').getBoundingClientRect();
                return Math.abs(last.getBoundingClientRect().bottom - close.bottom) < 2
                    && body.bottom <= close.top + 1
                    && [...document.querySelectorAll('.document-sheet')].every(p => p.scrollHeight <= p.clientHeight + 1);
            }
            """));
        var directory = Path.Combine(Path.GetTempPath(), "AlfaCore-DocumentPaginationTests");
        Directory.CreateDirectory(directory);
        await page.PdfAsync(new() { Path = Path.Combine(directory, $"{count}-{size}-{orientation}-{longRow}.pdf"), PreferCSSPageSize = true, PrintBackground = true });
    }

    [Fact]
    public void DesmarcadoConservaFlujoSinPaginador()
    {
        var template = DocumentTemplateDefinition.CrearCotizacionEstandar();
        Assert.False(template.TotalesAlPiePagina);
        var html = new DocumentRenderer().RenderCotizacion(template, new());
        Assert.DoesNotContain("document-closing", html);
        Assert.DoesNotContain("alfaDocumentReady", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CotizacionConFirmaYPortadaUsaMismaPaginacionEnServicioPdf(bool portada)
    {
        var template = DocumentTemplateDefinition.CrearCotizacionEstandar();
        template.TotalesAlPiePagina = true;
        template.Paper.MarginLeftMm = 18;
        template.Paper.MarginBottomMm = 15;
        var png = new ArcaQrService().GeneratePng(new(new DateTime(2026, 9, 14), "30000000007", 1, 1, 1,
            121, "PES", 1, 99, "0", "E", "70417054367476"));
        var data = new CotizacionDocumentData
        {
            IncluyePortada = portada, PortadaBytes = png, FirmaBytes = png, FirmanteNombre = "Firma de prueba",
            PropuestaHtml = "<p>Propuesta de prueba</p>",
            Items = Enumerable.Range(1, 60).Select(i => new CotizacionDocumentItemData
            { Codigo = i.ToString(), Descripcion = "Detalle de cotización " + i, Cantidad = 1, Total = 100, ImpactaTotal = true }).ToList(),
            Totales = new() { Neto = 6000, Total = 6000 }
        };
        using var provider = new ServiceCollection().BuildServiceProvider();
        await using var service = new DocumentPdfService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentPdfService>.Instance);
        var html = await service.PrepareHtmlAsync(new DocumentRenderer().RenderCotizacion(template, data));
        Assert.Contains("pagination-ready=\"true\"", html);
        Assert.DoesNotContain("<script>", html);
        var pdf = await service.GenerateAsync(html, new(true, true, "Empresa de prueba"));
        var path = Path.Combine(Path.GetTempPath(), "AlfaCore-DocumentPaginationTests", $"cotizacion-portada-{portada}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, pdf);
        Assert.True(pdf.Length > 1000);
    }
}
