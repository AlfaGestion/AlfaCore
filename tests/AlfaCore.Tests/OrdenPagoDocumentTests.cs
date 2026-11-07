using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Orden de pago (OPG) y pago proforma (PGFP) en el Diseñador de comprobantes.</summary>
[Collection("Conciliacion SQL")]
public sealed class OrdenPagoDocumentTests(ITestOutputHelper output)
{
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler; return proxy;
    }

    private static FacturaDocumentData Orden(string tipo = TiposDocumentoCore.OrdenPago, byte[]? logo = null) => new()
    {
        Empresa = new EmpresaDocumentData { Nombre = "ALFA NET <SRL>", Cuit = "30-11111111-1", Domicilio = "Calle 1", Logo = logo },
        EtiquetaTercero = "Proveedor",
        Comprobante = new FacturaComprobanteDocumentData { TipoDocumento = tipo, Tc = tipo == TiposDocumentoCore.OrdenPago ? "OPG" : "PGFP", Letra = "X", PuntoVenta = "0003", Numero = "00004190", Fecha = new DateTime(2026, 11, 6) },
        Cliente = new FacturaClienteDocumentData { Codigo = "211010103", RazonSocial = "EXPERTA & SEGUROS", DocumentoTipoDescripcion = "CUIT", DocumentoNumero = "30-71459054-1" },
        Items = [new() { Codigo = "FCC", Descripcion = "0008-00002752-A · 01/09/2022", Cantidad = 1, PrecioUnitario = 7489.17m, Subtotal = 7489.17m }],
        Totales = new FacturaTotalesDocumentData { Total = 7096.89m },
        MediosPago = [new("Caja Efectivo", 7096.89m)],
        CreditosAplicados = [new("NCC 0001-00002752-A", 392.28m)]
    };

    [Fact]
    public void LaOrdenDePagoYElPagoProformaSonTiposDelDisenador()
    {
        Assert.Equal(TiposDocumentoCore.OrdenPago, TiposDocumentoCore.TipoParaComprobante("OPG", "X"));
        Assert.Equal(TiposDocumentoCore.PagoProforma, TiposDocumentoCore.TipoParaComprobante("PGFP", "X"));
        Assert.True(TiposDocumentoCore.EsOrdenPago(TiposDocumentoCore.OrdenPago) && TiposDocumentoCore.EsOrdenPago(TiposDocumentoCore.PagoProforma));
        Assert.False(TiposDocumentoCore.EsOrdenPago(TiposDocumentoCore.FacturaA));
        Assert.Contains(TiposDocumentoCore.OrdenPago, TiposDocumentoCore.Todos);
        Assert.Equal("Orden de pago", TiposDocumentoCore.NombrePredeterminado(TiposDocumentoCore.OrdenPago));
        Assert.Equal("Pago proforma", TiposDocumentoCore.NombrePredeterminado(TiposDocumentoCore.PagoProforma));
        // Las cobranzas y facturas no cambian.
        Assert.Equal(TiposDocumentoCore.CobranzaX, TiposDocumentoCore.TipoParaComprobante("CB", "X"));
        Assert.Equal(TiposDocumentoCore.CobranzaProforma, TiposDocumentoCore.TipoParaComprobante("FP", "X"));
    }

    [Theory]
    [InlineData(TiposDocumentoCore.OrdenPago)]
    [InlineData(TiposDocumentoCore.PagoProforma)]
    public void LaPlantillaEstandarEsValidaYSobreviveAlJson(string tipo)
    {
        var servicio = new DocumentTemplateService(new ConfigurationBuilder().Build(), Proxy<IAppEventService>((_, _) => null),
            Proxy<IAppUserSessionService>((_, _) => null), NullLogger<DocumentTemplateService>.Instance, Proxy<ISessionService>((_, _) => null));
        var json = JsonSerializer.Serialize(DocumentTemplateDefinition.CrearOrdenPagoEstandar());
        var definicion = servicio.DeserializeAndValidate(json, tipo);
        Assert.Contains(definicion.Blocks, b => b.Type == TiposBloqueDocumento.MediosPago && b.Visible);
        Assert.Contains(definicion.Blocks, b => b.Type == TiposBloqueDocumento.Logo && b.CombineWithCompany);
    }

    [Fact]
    public void ElDisenoImprimeLogoProveedorComprobantesCanceladosMediosYTotalPagado()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var html = new DocumentRenderer().RenderFactura(DocumentTemplateDefinition.CrearOrdenPagoEstandar(), Orden(logo: png), null, null, "Orden de pago");
        Assert.Contains("ORDEN DE PAGO 0003-00004190", html);
        Assert.Contains("<strong>Proveedor</strong>", html);
        Assert.Contains("EXPERTA &amp; SEGUROS", html);
        Assert.Contains("Comprobante cancelado", html);
        Assert.Contains("0008-00002752-A", html);
        Assert.Contains("Importe aplicado", html);
        Assert.Contains("Medio de pago", html);
        Assert.Contains("Caja Efectivo", html);
        Assert.Contains("Aplicado: NCC 0001-00002752-A", html);
        Assert.Contains("Total pagado", html);
        Assert.Contains("7.096,89", html);
        Assert.Contains("<img", html);                       // el logo de la empresa
        Assert.Contains("ALFA NET &lt;SRL&gt;", html);       // escapado
        Assert.DoesNotContain("Cond. venta", html);
    }

    [Fact]
    public void ElBloqueDeMediosSePuedeOcultarYLasOtrasPlantillasConservanSuEtiqueta()
    {
        var plantilla = DocumentTemplateDefinition.CrearOrdenPagoEstandar();
        plantilla.Blocks.First(b => b.Type == TiposBloqueDocumento.MediosPago).Visible = false;
        Assert.DoesNotContain("Total pagado", new DocumentRenderer().RenderFactura(plantilla, Orden(), null, null, "Orden de pago"));

        // Una factura sigue diciendo "Cliente".
        var factura = Orden(TiposDocumentoCore.FacturaA); factura.EtiquetaTercero = "Cliente";
        var htmlFactura = new DocumentRenderer().RenderFactura(DocumentTemplateDefinition.CrearFacturaEstandar("A"), factura);
        Assert.Contains("<strong>Cliente</strong>", htmlFactura);
        Assert.DoesNotContain("Total pagado", htmlFactura);
    }

    [OrdenPagoSqlFact]
    public async Task ElDisenadorImprimeLasOrdenesRealesDeLaBase()
    {
        var connection = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")!;
        var dir = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_HTML_DIR");
        using var runtime = new CargaComprobantesArcaRuntime(); var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch { "GetConnectionString" => connection, "GetActiveSession" => sesion, _ => null });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => "Administrador", _ => null });
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var loggers = provider.GetRequiredService<ILoggerFactory>();
        var servicio = new OrdenPagoDocumentService(new ConfigurationBuilder().Build(),
            new DocumentTemplateService(new ConfigurationBuilder().Build(), runtime.Events, users, loggers.CreateLogger<DocumentTemplateService>(), sessions),
            new DocumentRenderer(), provider.GetRequiredService<DocumentPdfService>(), runtime.Events, loggers.CreateLogger<OrdenPagoDocumentService>(), sessions);

        var recientes = (await servicio.SearchRecientesAsync(TiposDocumentoCore.OrdenPago, 5)).Concat(await servicio.SearchRecientesAsync(TiposDocumentoCore.PagoProforma, 3)).ToList();
        Assert.NotEmpty(recientes);
        Assert.All(await servicio.SearchRecientesAsync(TiposDocumentoCore.OrdenPago, 5), r => Assert.Equal("OPG", r.Tc));
        Assert.Empty(await servicio.SearchRecientesAsync(TiposDocumentoCore.FacturaA));

        using var playwright = dir is null ? null : await Playwright.CreateAsync();
        foreach (var r in recientes)
        {
            var resultado = await servicio.RenderAsync(r.Tc, r.IdComprobante, null);
            Assert.Equal(r.Tc == "OPG" ? TiposDocumentoCore.OrdenPago : TiposDocumentoCore.PagoProforma, resultado.TipoDocumento);
            Assert.Contains(r.Tc == "OPG" ? "ORDEN DE PAGO" : "PAGO PROFORMA", resultado.Html);
            Assert.Contains("<strong>Proveedor</strong>", resultado.Html);
            output.WriteLine($"{r.Tc} {r.IdComprobante}: plantilla {resultado.IdTemplate}, {resultado.Html.Length} caracteres de HTML");
            if (dir is null) continue;
            Directory.CreateDirectory(dir);
            await using var browser = await playwright!.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = CargaComprobantesBrowserFactAttribute.BrowserPath });
            var page = await browser.NewPageAsync(); await page.SetViewportSizeAsync(900, 1000);
            await page.SetContentAsync(resultado.Html);
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"diseno-{r.Tc}-{r.IdComprobante}.png"), FullPage = true });
        }
        // La orden que tiene asiento muestra sus medios y los comprobantes cancelados.
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        var conMedios = recientes.FirstOrDefault(r => r.Tc == "OPG");
        var armado = await OrdenPagoDocumentService.BuildDataAsync(cn, conMedios!.Tc, conMedios.IdComprobante, CancellationToken.None);
        Assert.NotNull(armado);
        Assert.Equal("Proveedor", armado!.Datos.EtiquetaTercero);
        Assert.Equal('X', armado.Datos.Comprobante.Letra[0]);
    }
}
