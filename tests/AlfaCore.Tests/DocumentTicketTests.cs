using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

public sealed class DocumentTicketTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(40, true)]
    public async Task TicketEsAngostoSinDesbordesYConAlturaSegunDetalle(int items, bool totalesAlPie)
    {
        var template = DocumentTemplateDefinition.CrearFacturaEstandar("A");
        template.Paper = new() { Size = "Ticket80", MarginTopMm = 3, MarginBottomMm = 3, MarginLeftMm = 3, MarginRightMm = 3 };
        template.TotalesAlPiePagina = totalesAlPie;
        template.Blocks.Single(b => b.Type == TiposBloqueDocumento.Pie).Visible = true;
        var data = new FacturaDocumentData
        {
            Empresa = new() { Nombre = "Empresa con nombre comercial extenso", Cuit = "30-00000000-7" },
            Comprobante = new() { TipoDocumento = TiposDocumentoCore.DebitoA, Letra = "A", CodigoAfip = "002", Numero = "00000001", PuntoVenta = "00001", Fecha = new DateTime(2026, 9, 14) },
            Items = Enumerable.Range(1, items).Select(i => new FacturaDocumentItemData
            { Codigo = i.ToString(), Descripcion = "Artículo con descripción extensa para verificar el ancho del ticket de ochenta milímetros", Cantidad = 2, PrecioUnitario = 1000000, Subtotal = 2000000 }).ToList(),
            Totales = new() { NetoGravado = 2000000 * items, Total = 2000000 * items },
            Cae = new() { Cae = "70417054367476", Resultado = "A", VencimientoCae = new DateTime(2026, 9, 24) },
            QrBytes = new ArcaQrService().GeneratePng(new(new DateTime(2026, 9, 14), "30000000007", 1, 2, 1, 2000000 * items, "PES", 1, 99, "0", "E", "70417054367476"))
        };
        var definitions = new DocumentTemplateService(null!, null!, null!, null!);
        definitions.DeserializeAndValidate(JsonSerializer.Serialize(template), TiposDocumentoCore.DebitoA);
        using var provider = new ServiceCollection().BuildServiceProvider();
        await using var service = new DocumentPdfService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentPdfService>.Instance);
        var html = await service.PrepareHtmlAsync(new DocumentRenderer().RenderFactura(template, data));
        Assert.DoesNotContain("document-sheet", html);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const main = document.querySelector('main.doc').getBoundingClientRect();
                return Math.abs(main.width - 80 * 96 / 25.4) < 1
                    && [...document.querySelectorAll('main.doc *')].every(e => {
                        const r = e.getBoundingClientRect();
                        return !r.width || (r.left >= main.left - 1 && r.right <= main.right + 1);
                    });
            }
            """));
        Assert.Equal(items, await page.Locator(".items tbody tr").CountAsync());
        Assert.Equal(1, await page.Locator(".totals").CountAsync());
        Assert.Equal(1, await page.Locator(".qr-box img").CountAsync());
        var pdf = await service.GenerateAsync(html, new(true, true, data.Empresa.Nombre));
        var directory = Path.Combine(Path.GetTempPath(), "AlfaCore-DocumentTicketTests");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"ticket-{items}.pdf"), pdf);
    }

    [Fact]
    public void A4VerticalContinuaPredeterminadoYTicketNoAdmiteHorizontal()
    {
        var template = DocumentTemplateDefinition.CrearFacturaEstandar("A");
        Assert.Equal("A4", template.Paper.Size);
        Assert.Equal("Portrait", template.Paper.Orientation);
        template.Paper.Size = "Ticket80";
        template.Paper.Orientation = "Landscape";
        var service = new DocumentTemplateService(null!, null!, null!, null!);
        Assert.Throws<InvalidOperationException>(() => service.DeserializeAndValidate(JsonSerializer.Serialize(template), TiposDocumentoCore.FacturaA));
    }
}
