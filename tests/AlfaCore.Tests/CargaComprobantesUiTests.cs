using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Components.Pages;
using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CargaComprobantesUiTests
{
    [Fact]
    public async Task CargaDirectaValidaDesdeHeaderConservaBorradorYExigeRevalidarCambios()
    {
        var services = new ServiceCollection(); services.AddLogging();
        var fake = new FakeService(); services.AddSingleton<ICargaComprobantesService>(fake);
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<ISessionService>(DispatchProxy.Create<ISessionService, EventOnlyProxy>());
        services.AddSingleton<IAppUserSessionService>(DispatchProxy.Create<IAppUserSessionService, EventOnlyProxy>());
        services.AddSingleton<IPageHeaderService, PageHeaderService>();
        services.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime, EventOnlyProxy>());
        var clientes = DispatchProxy.Create<ICuentasComercialesService, EventOnlyProxy>();
        CuentaComercialSaveRequest? alta = null;
        ((EventOnlyProxy)(object)clientes).Handler = (m, args) => m.Name switch
        {
            "GetLookupDataAsync" => Task.FromResult(new CuentaComercialLookupDataDto()),
            "GetSuggestedCodigoAsync" => Task.FromResult("CUIT"),
            "SaveAsync" => GuardarCliente((CuentaComercialSaveRequest)args![1]!),
            _ => null
        };
        Task<string> GuardarCliente(CuentaComercialSaveRequest request) { alta = request; return Task.FromResult("CUIT"); }
        var padron = DispatchProxy.Create<IArcaPadronService, EventOnlyProxy>();
        ((EventOnlyProxy)(object)padron).Handler = (_, args) => Task.FromResult(new ArcaPadronPersonaDto { Cuit = (string)args![0]!, RazonSocial = "Alta por CUIT" });
        services.AddSingleton(clientes); services.AddSingleton(padron);
        var activator = new CapturingActivator(); services.AddSingleton<IComponentActivator>(activator);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        string directHtml = "";
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<VentasCargaDirecta>(); var page = activator.Directa!;
            var header = provider.GetRequiredService<IPageHeaderService>();
            Assert.Equal("ventas", header.Current.ModuleKey); Assert.Equal("Carga directa", header.Current.Title);
            Assert.Contains(header.Current.Actions, x => x.Key == "nuevo-cliente");
            var lookup = activator.Cliente!;
            await InvokeAsync(lookup, "HandleInputAsync", new ChangeEventArgs { Value = "papelera" });
            Assert.Equal("papelera", fake.UltimaBusquedaCliente);
            Assert.Equal("papelera", page.GetType().GetField("_clienteTexto", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            await header.Current.Actions.Single(x => x.Key == "nuevo-cliente").OnClick!();
            var modal = activator.ClientesRapido!;
            Assert.True(modal.StartInNew);
            var nuevo = (CuentaComercialSaveRequest)modal.GetType().GetField("_nuevo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(modal)!;
            nuevo.NumeroDocumento = "30-71234567-1";
            await InvokeAsync(modal, "ConsultarArcaAsync");
            Assert.Equal("Alta por CUIT", nuevo.RazonSocial);
            await InvokeAsync(modal, "GuardarAsync");
            Assert.NotNull(alta); Assert.Equal("30712345671", alta.NumeroDocumento);
            Assert.Contains("Alta por CUIT", System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            Invoke(page, "ClienteChanged", new CargaComprobantesOpcionFiltro { Codigo = "1", Descripcion = "Cliente de prueba" });
            Invoke(page, "AgregarArticulo", new CrmCotizacionArticuloDto { IdArticulo = "PAPEL", Descripcion = "Resma", PrecioUnitarioConIva = 121 });
            page.GetType().GetField("_talonario", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, "2");
            var draft = (CargaComprobantesDirecta)page.GetType().GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            draft.Deposito = "1";
            await header.Current.Actions.Single(x => x.Key == "validar").OnClick!();
            Assert.Contains("Liquidación validada", System.Net.WebUtility.HtmlDecode(root.ToHtmlString())); Assert.False(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            Invoke(page, "PrecioChanged", draft.Lineas[0], "90");
            Invoke(page, "DescuentoChanged", draft.Lineas[0], "5");
            Assert.True(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            fake.FallarDirecta = true;
            await header.Current.Actions.Single(x => x.Key == "validar").OnClick!();
            Assert.True(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            Assert.Single(draft.Lineas); Assert.Equal(1m, draft.Lineas[0].Cantidad);
            Assert.Equal(90m, draft.Lineas[0].PrecioUnitarioNeto); Assert.Equal(5m, draft.Lineas[0].Descuento);
            Assert.Contains("Validación directa rechazada", System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            fake.FallarDirecta = false;
            await header.Current.Actions.Single(x => x.Key == "validar").OnClick!();
            Invoke(page, "CantidadChanged", draft.Lineas[0], "2");
            Assert.True(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            await header.Current.Actions.Single(x => x.Key == "validar").OnClick!();
            fake.FallarGrabacionDirecta = true;
            await InvokeAsync(page, "GrabarAsync");
            Assert.True(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            await header.Current.Actions.Single(x => x.Key == "recuperar").OnClick!();
            Assert.Equal(2m, Assert.Single(draft.Lineas).Cantidad);
            Assert.False(header.Current.Actions.Single(x => x.Key == "validar").Disabled);
            fake.FallarGrabacionDirecta = false;
            await header.Current.Actions.Single(x => x.Key == "validar").OnClick!();
            directHtml = root.ToHtmlString();
            await InvokeAsync(page, "GrabarAsync");
            Assert.Equal(1, fake.DirectasGrabadas); Assert.Contains("Comprobante grabado", root.ToHtmlString());
            Assert.True(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            await header.Current.Actions.Single(x => x.Key == "nueva").OnClick!();
            Invoke(page, "TipoChanged", "FP");
            Assert.Equal("Grabar proforma", header.Current.Actions.Single(x => x.Key == "grabar").Label);
        });
        if (CargaComprobantesBrowserFactAttribute.BrowserPath is not null)
        {
            var repo = FindRoot();
            var css = await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/wwwroot/css/alfacore-design.css")) +
                await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/wwwroot/app.css")) +
                await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/wwwroot/css/gestion-alfadesign.css")) +
                await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/wwwroot/css/modulos-alfadesign.css")) +
                await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = CargaComprobantesBrowserFactAttribute.BrowserPath });
            var page = await browser.NewPageAsync();
            foreach (var width in new[] { 2048, 1440, 1024 })
            {
                await page.SetViewportSizeAsync(width, 900);
                await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style><style>body{{margin:0;font-family:Segoe UI,sans-serif}}*{{box-sizing:border-box}}</style></head><body>{directHtml}</body></html>");
                Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
                Assert.True(await page.GetByLabel("Tipo de comprobante").IsVisibleAsync());
                Assert.True(await page.GetByLabel("Cantidad PAPEL", new() { Exact = true }).IsVisibleAsync());
                Assert.True(await page.GetByLabel("Precio neto PAPEL", new() { Exact = true }).IsEnabledAsync());
                Assert.True(await page.GetByLabel("Descuento PAPEL", new() { Exact = true }).IsEnabledAsync());
                await page.GetByLabel("Cantidad PAPEL", new() { Exact = true }).FocusAsync();
                Assert.Equal("number", await page.EvaluateAsync<string>("document.activeElement.type"));
                if (width == 1024)
                {
                    var destino = Path.Combine(repo, "artifacts/ventas/homologacion/carga-directa-1024.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(destino)!); await page.ScreenshotAsync(new() { Path = destino, FullPage = true });
                }
            }
        }
    }

    [CargaComprobantesBrowserFact]
    public async Task EditorSimulaConservaCargaAnteErrorYNoDesborda()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<ICargaComprobantesService, FakeService>();
        services.AddSingleton<ISessionService>(DispatchProxy.Create<ISessionService, EventOnlyProxy>());
        services.AddSingleton<IAppUserSessionService>(DispatchProxy.Create<IAppUserSessionService, EventOnlyProxy>());
        services.AddSingleton<IPageHeaderService, PageHeaderService>();
        services.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime, EventOnlyProxy>());
        var activator = new CapturingActivator();
        services.AddSingleton<IComponentActivator>(activator);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        string list = "", editor = "", filters = "";
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<VentasCargaComprobantes>();
            list = root.ToHtmlString();
            var component = activator.Page!;
            var header = provider.GetRequiredService<IPageHeaderService>();
            Assert.NotNull(header.Current.SearchContent);
            Assert.DoesNotContain("cc-filters", list);
            Invoke(component, "AlternarFiltros");
            var searchRoot = await renderer.RenderComponentAsync<FragmentHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(FragmentHost.Content)] = header.Current.SearchContent }));
            filters = searchRoot.ToHtmlString();
            Assert.Contains("Fecha desde", filters); Assert.Contains("Zona del cliente", filters);
            Assert.Contains("smart-search__panel--wide", filters);
            await header.Current.Actions.Single(x => x.Key == "electronica").OnClick!();
            Assert.Contains("Punto de venta 1007", root.ToHtmlString());
            var borrador = (CargaComprobantesFiltro)component.GetType().GetField("_filtro", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            borrador.FechaDesde = new DateTime(2024, 1, 1); borrador.FechaHasta = new DateTime(2024, 12, 31);
            borrador.Vendedor = "V1"; borrador.Zona = "SUR";
            await InvokeAsync(component, "BuscarAsync", true);
            var fake = (FakeService)provider.GetRequiredService<ICargaComprobantesService>();
            Assert.Equal("V1", fake.UltimoFiltro!.Vendedor);
            borrador.Vendedor = "V2";
            await InvokeAsync(component, "PaginarAsync", 1);
            Assert.Equal("V1", fake.UltimoFiltro.Vendedor); // No aplicar campos editados sin Buscar.
            await InvokeAsync(component, "QuitarFiltroAsync", "zona");
            Assert.Equal("V1", fake.UltimoFiltro.Vendedor);
            Assert.Equal("", fake.UltimoFiltro.Zona);
            await InvokeAsync(component, "LimpiarFiltrosAsync");
            Assert.Null(fake.UltimoFiltro.FechaDesde); Assert.Equal("", fake.UltimoFiltro.Zona);
            Assert.Equal(1, fake.UltimoFiltro.PageNumber);
            // HtmlRenderer no tiene circuito interactivo: invocar los mismos handlers
            // de los botones y renderizar el resultado permite probar conservación de estado.
            await InvokeAsync(component, "CargarAsync", 1);
            Invoke(component, "CambiarTalonario", "2");
            Invoke(component, "CambiarCantidad", 11, "2.5");
            await header.Current.Actions.Single(x => x.Key == "simular").OnClick!();
            editor = root.ToHtmlString();
            Assert.Contains("Simulación lista", System.Net.WebUtility.HtmlDecode(editor));
            Assert.Contains("302", editor);
            Assert.Contains("Total calculado", editor);
            Assert.Contains("IVA 21", editor);
            await InvokeAsync(component, "EmitirPruebaAsync");
            Assert.Contains("APROBADA", root.ToHtmlString());
            Assert.True(provider.GetRequiredService<IPageHeaderService>().Current.Actions.Single(x => x.Key == "prueba-arca").Disabled);
            Invoke(component, "CambiarCantidad", 11, "99");
            await header.Current.Actions.Single(x => x.Key == "simular").OnClick!();
            var error = root.ToHtmlString();
            Assert.Contains("value=\"99\"", error);
            Assert.Contains("PAPEL", error);
            Assert.DoesNotContain("cc-total", error);
            Invoke(component, "CambiarCantidad", 11, "2.5");
            await header.Current.Actions.Single(x => x.Key == "simular").OnClick!();
            await header.Current.Actions.Single(x => x.Key == "validar-grabacion").OnClick!();
            Assert.Contains("Grabación validada", System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
            await header.Current.Actions.Single(x => x.Key == "electronica").OnClick!();
            Assert.False(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            await InvokeAsync(component, "GrabarFacturaAsync");
            Assert.Contains("100700000186A", root.ToHtmlString());
            Assert.True(header.Current.Actions.Single(x => x.Key == "grabar").Disabled);
            Assert.False(header.Current.Actions.Single(x => x.Key == "pdf").Disabled);
            Assert.Equal("ventas", provider.GetRequiredService<IPageHeaderService>().Current.ModuleKey);
            Assert.Single(provider.GetRequiredService<IPageHeaderService>().Current.TopNavigationItems);
            borrador.Vendedor = "ANTERIOR"; borrador.FechaDesde = new DateTime(2024, 1, 1);
            Invoke(component, "ContextChanged");
            Assert.Equal("", borrador.Vendedor); Assert.Null(borrador.FechaDesde);
        });
        var repo = FindRoot();
        var css = await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/wwwroot/css/alfacore-design.css")) +
                  await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/obj/Debug/net8.0/scopedcss/projectbundle/AlfaCore.bundle.scp.css"));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true, ExecutablePath = CargaComprobantesBrowserFactAttribute.BrowserPath
        });
        var page = await browser.NewPageAsync();
        foreach (var html in new[] { list, editor })
        {
            foreach (var width in new[] { 2048, 1440, 1024 })
            {
                await page.SetViewportSizeAsync(width, 900);
                await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style><style>body{{margin:0;font-family:Segoe UI,sans-serif}}*{{box-sizing:border-box}}</style></head><body>{html}</body></html>");
                Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
                Assert.True(await page.Locator("table").CountAsync() > 0);
            }
        }
        await page.GetByLabel("Cantidad de PAPEL").FocusAsync();
        Assert.Equal("cc-cantidad-11", await page.EvaluateAsync<string>("document.activeElement.id"));
        await page.Keyboard.PressAsync("Tab");
        Assert.NotEqual("cc-cantidad-11", await page.EvaluateAsync<string>("document.activeElement.id"));
        var script = await File.ReadAllTextAsync(Path.Combine(repo, "src/AlfaCore/wwwroot/js/alfa-smart-search-popover.js"));
        foreach (var width in new[] { 2048, 1440, 1024 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await page.SetContentAsync($"<html><head><meta charset='utf-8'><style>{css}</style></head><body><div class='shell shell--alfa-design'><section class='main-page-header main-page-header--alfa-design' style='margin-left:30%;margin-top:80px'><div class='main-page-header__search'>{filters}</div></section></div></body></html>");
            await page.AddScriptTagAsync(new() { Content = script });
            await page.WaitForFunctionAsync("document.querySelector('.smart-search__panel').style.position === 'fixed'");
            var bounds = await page.Locator(".smart-search__panel").BoundingBoxAsync();
            Assert.NotNull(bounds);
            Assert.True(bounds.X >= 12 && bounds.X + bounds.Width <= width - 11);
            Assert.True(await page.GetByLabel("Fecha desde").IsVisibleAsync());
            Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Aplicar", Exact = true }).IsVisibleAsync());
        }
    }

    private static object? Invoke(object component, string method, params object?[] args)
        => component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
    private static Task InvokeAsync(object component, string method, params object?[] args)
        => (Task)Invoke(component, method, args)!;
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src/AlfaCore/AlfaCore.csproj"))) return dir.FullName;
        throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    public class EventOnlyProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is null ? null : Handler?.Invoke(targetMethod, args);
    }
    public sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost:5055/", "http://localhost:5055/ALFANET/153/ventas/carga-comprobantes");
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
    }
    public sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Content);
    }
    private sealed class CapturingActivator : IComponentActivator
    {
        public VentasCargaComprobantes? Page { get; private set; }
        public QuickClienteModal? ClientesRapido { get; private set; }
        public VentasCargaDirecta? Directa { get; private set; }
        public AlfaLookup<CargaComprobantesOpcionFiltro>? Cliente { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var instance = (IComponent)Activator.CreateInstance(componentType)!;
            if (instance is VentasCargaComprobantes page) Page = page;
            if (instance is QuickClienteModal modal) ClientesRapido = modal;
            if (instance is VentasCargaDirecta directa) Directa = directa;
            if (instance is AlfaLookup<CargaComprobantesOpcionFiltro> lookup && Cliente is null) Cliente = lookup;
            return instance;
        }
    }
    private sealed class FakeService : ICargaComprobantesService
    {
        public bool FallarDirecta { get; set; }
        public bool FallarGrabacionDirecta { get; set; }
        public int DirectasGrabadas { get; private set; }
        public Task<CargaComprobantesGrabacionResultado?> ConsultarDirectaAsync(Guid borrador, Guid operacion, CancellationToken ct = default)
            => Task.FromResult<CargaComprobantesGrabacionResultado?>(null);
        public Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarFacturasAsociadasAsync(string cliente, string unidad, string texto, CancellationToken ct = default) => throw new NotSupportedException();
        public string? UltimaBusquedaCliente { get; private set; }
        public Task<IReadOnlyList<CargaComprobantesOpcionFiltro>> BuscarClientesDirectaAsync(string texto, CancellationToken ct = default)
        { UltimaBusquedaCliente = texto; return Task.FromResult<IReadOnlyList<CargaComprobantesOpcionFiltro>>([new() { Codigo = "1", Descripcion = "Papelera de prueba" }]); }
        public Task<IReadOnlyList<CrmCotizacionArticuloDto>> BuscarArticulosDirectaAsync(string cliente, string texto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CargaComprobantesPlanGrabacion> ValidarDirectaAsync(CargaComprobantesDirecta directa, int talonario, CancellationToken ct = default)
        {
            if (FallarDirecta) throw new CargaComprobantesReglaException("Validación directa rechazada");
            var origen = new CargaComprobantesOrigen { Tc = "DIRECTA", Moneda = "1", DatosCalculoDisponibles = true,
                Lineas = directa.Lineas.Select((l, i) => new CargaComprobantesLineaOrigen { Id = i+1, Secuencia = i+1,
                    Articulo = l.Articulo, Descripcion = "Resma", CantidadOriginal = l.Cantidad, Pendiente = l.Cantidad, RenglonesArticulo = 1,
                    Precio = decimal.Round((l.PrecioUnitarioNeto ?? 100) * 1.21m,4), PrecioSinIva = l.PrecioUnitarioNeto ?? 100,
                    Descuento = l.Descuento ?? 0, PrecioReferenciaNeto = l.PrecioUnitarioNeto.HasValue ? 100 : null, AlicuotaIva = 21 }).ToList() };
            return Task.FromResult(new CargaComprobantesPlanGrabacion { Directa = directa, Origen = origen,
                Talonario = talonario, Letra = "A", PuntoVenta = 1007, SignoStock = -1, Fecha = new(2026,10,15), Vencimiento = new(2026,10,17),
                Liquidacion = CargaComprobantesCalculador.Calcular(origen, _context, origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.CantidadOriginal }).ToList()) });
        }
        public Task<CargaComprobantesGrabacionResultado> GrabarDirectaAsync(CargaComprobantesDirecta directa, int talonario, Guid operacion, string huellaEsperada, CancellationToken ct = default)
        { if (FallarGrabacionDirecta) throw new CargaComprobantesReglaException("Datos cambiaron antes de emitir");
            DirectasGrabadas++; return Task.FromResult(new CargaComprobantesGrabacionResultado { Operacion = operacion, Tc = directa.Tc, Estado = "GRABADA", Comprobante = "100700000187A", NumeroAsiento = 3 }); }
        public CargaComprobantesFiltro? UltimoFiltro { get; private set; }
        private readonly CargaComprobantesContexto _context = new()
        {
            PuedeEditarPrecio = true, PuedeEditarDescuento = true,
            UnidadNegocio = "4", UnidadesNegocio = [new() { Codigo = "4", Descripcion = "Distribuidora" }],
            Depositos = [new() { Codigo = "1", Descripcion = "Principal" }],
            TalonariosDirecta = [new() { Tc = "FC", Talonario = 2, Letras = "AB", Descripcion = "Factura" }],
            DescargaStockRemitos = true,
            PreciosFinales = true, AlicuotaGeneral = 21,
            Talonarios = [new() { Talonario = 2, Letras = "AB", Descripcion = "Factura" }]
        };
        private readonly CargaComprobantesOrigen _origen = new()
        {
            Id = 1, Tc = "RM", Comprobante = "000100000001X", Cliente = "Distribuidora de prueba",
            Moneda = "1", TieneStock = true,
            DatosCalculoDisponibles = true,
            Lineas = [new() { Id = 11, Articulo = "PAPEL", Descripcion = "Resma de papel", Unidad = "UN", CantidadOriginal = 10,
                Pendiente = 4, RenglonesArticulo = 1, TotalOriginal = 1210, Precio = 121, PrecioSinIva = 100, AlicuotaIva = 21 }]
        };
        public Task<CargaComprobantesContexto> GetContextoAsync(CancellationToken ct = default) => Task.FromResult(_context);
        public Task<byte[]> GetPdfFacturaAsync(Guid operacion, CancellationToken ct = default) => Task.FromResult<byte[]>([]);
        public Task<CargaComprobantesPlanGrabacion> ValidarGrabacionAsync(int origenId, int talonario,
            IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default)
            => Task.FromResult(new CargaComprobantesPlanGrabacion { OrigenId = origenId, Origen = _origen, Talonario = talonario, PuntoVenta = 1007, Liquidacion = CargaComprobantesCalculador.Calcular(_origen, _context, cantidades) });
        public Task<CargaComprobantesGrabacionResultado> GrabarFacturaAsync(int origenId, int talonario,
            IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion, string huellaEsperada, CancellationToken ct = default)
            => Task.FromResult(new CargaComprobantesGrabacionResultado { Operacion = operacion, OrigenId = origenId, Estado = "GRABADA", Comprobante = "100700000186A", NumeroAsiento = 2 });
        public Task<CargaComprobantesGrabacionResultado> RecuperarGrabacionAsync(Guid operacion, CancellationToken ct = default)
            => Task.FromResult(new CargaComprobantesGrabacionResultado { Operacion = operacion, Estado = "GRABADA" });
        public Task<CargaComprobantesGrabacionResultado?> GetUltimaGrabacionAsync(int origenId, CancellationToken ct = default)
            => Task.FromResult<CargaComprobantesGrabacionResultado?>(null);
        public Task<IReadOnlyList<CargaComprobantesGrabacionResultado>> GetGrabacionesRecientesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CargaComprobantesGrabacionResultado>>([]);
        public Task<CargaComprobantesElectronicaPrueba> RecuperarPruebaElectronicaAsync(Guid operacion, CancellationToken ct = default)
            => Task.FromResult(new CargaComprobantesElectronicaPrueba { Operacion = operacion, Estado = "APROBADA" });
        public Task<CargaComprobantesElectronicaPrueba?> GetUltimaPruebaElectronicaAsync(int origenId, CancellationToken ct = default)
            => Task.FromResult<CargaComprobantesElectronicaPrueba?>(null);
        public Task<CargaComprobantesElectronicaEstado> VerificarElectronicaAsync(int? origenId = null, CancellationToken ct = default)
            => Task.FromResult(new CargaComprobantesElectronicaEstado { Ambiente = "HOMOLOGACION", PuntoVenta = 1007 });
        public Task<CargaComprobantesElectronicaPrueba> EmitirPruebaElectronicaAsync(int origenId, int talonario,
            IReadOnlyList<CargaComprobantesCantidad> cantidades, Guid operacion, CancellationToken ct = default)
            => Task.FromResult(new CargaComprobantesElectronicaPrueba { Operacion = operacion, OrigenId = origenId, Estado = "APROBADA", PuntoVenta = 1007, Numero = 185, Importe = 302.5m });
        public Task<PagedResult<CargaComprobantesOrigen>> BuscarOrigenesAsync(CargaComprobantesFiltro filtro, CancellationToken ct = default)
        {
            UltimoFiltro = filtro;
            return Task.FromResult(new PagedResult<CargaComprobantesOrigen> { Items = [_origen], Total = 1, PageNumber = filtro.PageNumber, PageSize = 25 });
        }
        public Task<CargaComprobantesOrigen> GetOrigenAsync(int id, CancellationToken ct = default) => Task.FromResult(_origen);
        public Task<CargaComprobantesSimulacion> SimularAsync(int id, int talonario, IReadOnlyList<CargaComprobantesCantidad> cantidades, CancellationToken ct = default)
            => Task.FromResult(CargaComprobantesSimulador.Simular(_origen, _context, cantidades));
    }
}

public sealed class CargaComprobantesBrowserFactAttribute : FactAttribute
{
    public static string? BrowserPath => Environment.GetEnvironmentVariable("ALFACORE_TEST_BROWSER") ??
        new[] { @"C:\Program Files\Google\Chrome\Application\chrome.exe", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" }.FirstOrDefault(File.Exists);
    public CargaComprobantesBrowserFactAttribute()
    {
        if (BrowserPath is null) Skip = "Requiere Chromium instalado o ALFACORE_TEST_BROWSER.";
    }
}
