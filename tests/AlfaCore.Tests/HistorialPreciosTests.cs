using System.Reflection;
using AlfaCore.Components.Pages;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class HistorialPreciosTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("costo", "COSTO")]
    [InlineData(" precio8 ", "PRECIO8")]
    public void ColumnaAceptaSoloCamposConocidos(string campo, string esperado) => Assert.Equal(esperado, HistorialPreciosService.Columna(campo));

    [Theory]
    [InlineData("PRECIO9")]
    [InlineData("COSTO; DROP TABLE X")]
    [InlineData("")]
    public void ColumnaRechazaCualquierOtroTexto(string campo) => Assert.ThrowsAny<InvalidOperationException>(() => HistorialPreciosService.Columna(campo));

    [Fact]
    public void ValidarRechazaPeriodoInvertidoOExcesivo()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => HistorialPreciosService.Validar(new HistorialPrecioFiltro { Desde = new(2026, 2, 1), Hasta = new(2026, 1, 1) }));
        Assert.ThrowsAny<InvalidOperationException>(() => HistorialPreciosService.Validar(new HistorialPrecioFiltro { Desde = new(2020, 1, 1), Hasta = new(2026, 1, 1) }));
        HistorialPreciosService.Validar(new HistorialPrecioFiltro { Desde = new(2026, 1, 1), Hasta = new(2026, 1, 1) });
    }

    [Fact]
    public void EncadenarCompletaMesesSinCambiosYPesaTodoElAlcance()
    {
        // 100 artículos: en enero 50 suben 10 %, febrero sin cambios, en marzo los otros 50 suben 10 %.
        var ln = 50 * Math.Log(1.10);
        var r = HistorialPreciosService.Encadenar(
            [new() { Periodo = new(2026, 1, 1), SumaLn = ln }, new() { Periodo = new(2026, 3, 1), SumaLn = ln }],
            new(2026, 1, 15), new(2026, 3, 20), porDia: false, universo: 100);

        Assert.Equal(3, r.Count);
        Assert.Equal(0, r[1].Variacion, 6);
        Assert.Equal(r[0].Acumulada, r[1].Acumulada, 6);
        Assert.Equal(10, r[2].Acumulada, 6); // Todos subieron 10 %: el acumulado es 10 %, no 21 %.
    }

    [Fact]
    public void EncadenarConAlcanceVacioNoFalla()
        => Assert.All(HistorialPreciosService.Encadenar([], new(2026, 1, 1), new(2026, 1, 3), porDia: true, universo: 0), x => Assert.Equal(0, x.Acumulada));

    [Fact]
    public void SoloCambiosDescartaFotosIgualesYOtrasUnidades()
    {
        var r = HistorialPreciosService.SoloCambios(
        [
            new() { IdUnidad = "UN", Costo = 10, Precio1 = 20 },
            new() { IdUnidad = "UN", Costo = 10, Precio1 = 20 },
            new() { IdUnidad = "CJ", Costo = 99, Precio1 = 99 },
            new() { IdUnidad = "UN", Costo = 12, Precio1 = 20 }
        ]);
        Assert.Equal([10m, 12m], r.Select(x => x.Costo));
    }

    [Fact]
    public void SqlDelHistorialEsCompatibleConBasesViejas()
    {
        var sql = HistorialPreciosService.SqlBase(new HistorialPrecioFiltro { Origen = "MAY" }, "PRECIO1", "PRECIO1");
        foreach (var prohibido in new[] { "LAG(", "LEAD(", "PERCENTILE", "TRY_CONVERT", "IIF(", "DATEFROMPARTS", "FORMAT(" })
            Assert.DoesNotContain(prohibido, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LTRIM(RTRIM(h.IdLista)) = @Lista", sql);
        Assert.DoesNotContain("MAY", sql);
    }

    [Fact]
    public void ClaseDeReferenciaUsaLaDeVentaSoloSiEstaEnUso()
    {
        var usados = new Dictionary<int, int> { [1] = 100, [3] = 900, [4] = 0 };
        Assert.Equal(3, HistorialPreciosService.ClaseDeReferencia(4, usados, 1000));
        Assert.Equal(1, HistorialPreciosService.ClaseDeReferencia(1, new Dictionary<int, int> { [1] = 600, [3] = 900 }, 1000));
    }

    [Fact]
    public void EtiquetasDelEjeNoSePisanConPuntosAmontonados()
        => Assert.Equal([0, 3, 4], HistorialPrecioChart.EtiquetasVisibles([0, 0.01, 0.02, 0.5, 0.9, 0.95]));

    [Fact]
    public void EscalaDelGraficoContieneLosDatosConPasosRedondos()
    {
        var (min, max, ticks) = HistorialPrecioChart.Escala(-3.2, 47.5);
        Assert.True(min <= -3.2 && max >= 47.5);
        Assert.Contains(0d, ticks);
        Assert.InRange(ticks.Count, 3, 8);
    }

    private static ServiceProvider Servicios(Func<System.Reflection.MethodInfo, object?[]?, object?> historial, IComponentActivator activator)
    {
        var svc = DispatchProxy.Create<IHistorialPreciosService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)svc).Handler = historial;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(svc);
        services.AddSingleton<IArticulosService>(DispatchProxy.Create<IArticulosService, CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton<NavigationManager, CargaComprobantesUiTests.TestNavigation>();
        services.AddSingleton<IRouteContextService, RouteContextService>();
        var modo = DispatchProxy.Create<IAppModeService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)modo).Handler = (m, _) => m.Name == "get_IsSaaSMode" ? true : null;
        services.AddSingleton(modo);
        services.AddSingleton<ISessionService>(DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton<IAppUserSessionService>(DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton<IPageHeaderService, PageHeaderService>();
        services.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime, CargaComprobantesUiTests.EventOnlyProxy>());
        services.AddSingleton(activator);
        return services.BuildServiceProvider();
    }

    private sealed class Captura(string solapa) : IComponentActivator
    {
        public ArticulosHistorialPrecios? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var c = (IComponent)Activator.CreateInstance(type)!;
            if (c is ArticulosHistorialPrecios p)
            {
                Page = p;
                typeof(ArticulosHistorialPrecios).GetField("_solapa", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(p, solapa);
            }
            return c;
        }
    }

    private static HistorialPrecioContextoDto Contexto() => new()
    {
        ClaseVenta = 1, Campos = [new("COSTO", "Costo"), new("PRECIO1", "Mayorista")], Listas = [new("MAY", "MAY · Mayorista")],
        Rubros = [new("01", "Almacén")], Marcas = [new("02", "Marca")]
    };

    [Fact]
    public async Task PantallaMuestraResumenGraficosYFiltroDeProveedor()
    {
        var periodos = new List<HistorialPrecioPeriodoDto>
        {
            new() { Periodo = new(2026, 9, 1), Cambios = 10, Articulos = 8, Variacion = 3, Acumulada = 3 },
            new() { Periodo = new(2026, 10, 1), Cambios = 4, Articulos = 4, Variacion = -1, Acumulada = 2 }
        };
        var resumen = new HistorialPrecioResumenDto
        {
            Cambios = 14, Articulos = 9, Universo = 100, Aumentos = 10, Bajas = 4, Mediana = 5, VariacionAcumulada = 2, CampoComparado = "PRECIO1", VariacionComparada = 1,
            Periodos = periodos, PeriodosComparados = periodos,
            Proveedores = [new() { Codigo = "2001", Nombre = "PROVEEDOR UNO", Cambios = 14, Articulos = 9, Variacion = 2 }]
        };
        var activator = new Captura("resumen");
        using var provider = Servicios((m, _) => m.Name switch
        {
            "GetContextoAsync" => Task.FromResult(Contexto()),
            "GetResumenAsync" => Task.FromResult(resumen),
            _ => null
        }, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<ArticulosHistorialPrecios>();
            var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Variación acumulada", html);
            Assert.Contains("PROVEEDOR UNO", html);
            Assert.Contains("hpc__linea", html);
            Assert.Contains("hpc__barra", html);
            var header = provider.GetRequiredService<IPageHeaderService>().Current;
            Assert.Equal("Historial de precios", header.Title);
            Assert.Contains(header.TopNavigationItems, x => x.Key == "historial" && x.Active);

            typeof(ArticulosHistorialPrecios).GetMethod("AlternarFiltros", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page, null);
            var panel = await renderer.RenderComponentAsync<CargaComprobantesUiTests.FragmentHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Content"] = provider.GetRequiredService<IPageHeaderService>().Current.SearchContent }));
            var filtros = System.Net.WebUtility.HtmlDecode(panel.ToHtmlString());
            Assert.Contains("Qué controlar", filtros);
            Assert.Contains("Proveedor", filtros);
            Assert.Contains("Aplicar", filtros);
        });
    }

    /// <summary>Con ALFACORE_TEST_OUT deja el HTML de cada solapa con datos reales, para revisarlo en un navegador.</summary>
    [ProduccionSoloLecturaFact]
    public async Task PantallasConDatosReales()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_PROD_SQL"));
        await cn.OpenAsync();
        var salida = Environment.GetEnvironmentVariable("ALFACORE_TEST_OUT");
        foreach (var solapa in new[] { "resumen", "cambios", "articulos", "alertas" })
        {
            var activator = new Captura(solapa);
            using var provider = Servicios((m, a) => m.Name switch
            {
                "GetContextoAsync" => HistorialPreciosService.LeerContextoAsync(cn, default),
                "GetResumenAsync" => HistorialPreciosService.LeerResumenAsync(cn, (HistorialPrecioFiltro)a![0]!, default),
                "GetCambiosAsync" => HistorialPreciosService.LeerCambiosAsync(cn, (HistorialPrecioFiltro)a![0]!, default),
                "GetArticulosAsync" => HistorialPreciosService.LeerArticulosAsync(cn, (HistorialPrecioFiltro)a![0]!, default),
                "GetAlertasAsync" => HistorialPreciosService.LeerAlertasAsync(cn, (HistorialPrecioFiltro)a![0]!, default),
                "GetFichaAsync" => HistorialPreciosService.LeerFichaAsync(cn, (string)a![0]!, (HistorialPrecioFiltro)a[1]!, default),
                _ => null
            }, activator);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<ArticulosHistorialPrecios>();
                var html = root.ToHtmlString();
                Assert.DoesNotContain("No se pudo", System.Net.WebUtility.HtmlDecode(html));
                if (!string.IsNullOrEmpty(salida)) File.WriteAllText(Path.Combine(salida, $"hp-{solapa}.html"), html);
                output.WriteLine($"{solapa}: {html.Length} caracteres");
                if (solapa == "cambios")
                {
                    var primera = ((HistorialPrecioPagina<HistorialPrecioCambioDto>)typeof(ArticulosHistorialPrecios)
                        .GetField("_cambios", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(activator.Page)!).Filas[0];
                    await (Task)typeof(ArticulosHistorialPrecios).GetMethod("AbrirFichaAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page, [primera.IdArticulo])!;
                    var ficha = root.ToHtmlString();
                    Assert.Contains("hpc__linea", ficha);
                    if (!string.IsNullOrEmpty(salida)) File.WriteAllText(Path.Combine(salida, "hp-ficha.html"), ficha);
                }
            });
        }
    }

    [ProduccionSoloLecturaFact]
    public async Task ExcelDeCadaSolapa()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_PROD_SQL"));
        await cn.OpenAsync();
        var f = new HistorialPrecioFiltro { Desde = DateTime.Today.AddDays(-30), Hasta = DateTime.Today };
        foreach (var solapa in new[] { "resumen", "cambios", "articulos", "alertas" })
        {
            using var ms = new MemoryStream(await HistorialPreciosService.ArmarExcelAsync(cn, solapa, f, default));
            using var wb = new ClosedXML.Excel.XLWorkbook(ms);
            var filas = wb.Worksheet(1).LastRowUsed()!.RowNumber();
            output.WriteLine($"{solapa}: {filas} filas");
            Assert.True(filas > 1);
        }
    }

    [ProduccionSoloLecturaFact]
    public async Task LecturaContraBaseReal()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_PROD_SQL"));
        await cn.OpenAsync();
        var hoy = DateTime.Today;
        foreach (var f in new[]
        {
            new HistorialPrecioFiltro { Desde = hoy.AddMonths(-12), Hasta = hoy, Campo = "COSTO" },
            new HistorialPrecioFiltro { Desde = hoy.AddMonths(-12), Hasta = hoy, Campo = "PRECIO1" },
            new HistorialPrecioFiltro { Desde = hoy.AddMonths(-12), Hasta = hoy, Campo = "PRECIO6" },
            new HistorialPrecioFiltro { Desde = hoy.AddMonths(-12), Hasta = hoy, Campo = "PRECIO1", Origen = "MAY" },
            new HistorialPrecioFiltro { Desde = hoy.AddDays(-30), Hasta = hoy, Campo = "COSTO" }
        })
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await HistorialPreciosService.LeerResumenAsync(cn, f, default);
            var msResumen = sw.ElapsedMilliseconds; sw.Restart();
            var c = await HistorialPreciosService.LeerCambiosAsync(cn, f, default);
            var msCambios = sw.ElapsedMilliseconds; sw.Restart();
            var a = await HistorialPreciosService.LeerArticulosAsync(cn, f, default);
            var msArticulos = sw.ElapsedMilliseconds; sw.Restart();
            var al = await HistorialPreciosService.LeerAlertasAsync(cn, f, default);
            var msAlertas = sw.ElapsedMilliseconds;
            output.WriteLine($"{f.Campo}/{(f.Origen.Length == 0 ? "maestro" : f.Origen)} {f.Desde:dd/MM/yy}: cambios {r.Cambios} articulos {r.Articulos}/{r.Universo} sube {r.Aumentos} baja {r.Bajas} atipicos {r.Atipicos} mediana {r.Mediana:N2} acumulada {r.VariacionAcumulada:N2} vs {r.CampoComparado} {r.VariacionComparada:N2}");
            output.WriteLine($"   ms: resumen {msResumen} cambios {msCambios} articulos {msArticulos} alertas {msAlertas} | alertas: {string.Join(", ", al.Conteos.Select(x => $"{x.Tipo}={x.Cantidad}"))}");
            Assert.Equal(r.Cambios, c.Total);
            Assert.Equal(r.Articulos, a.Total);
            Assert.Equal(r.Cambios, r.Periodos.Sum(x => x.Cambios));
            Assert.Equal(r.Cambios, r.Aumentos + r.Bajas);
            if (c.Filas.Count > 0)
            {
                var ficha = await HistorialPreciosService.LeerFichaAsync(cn, c.Filas[0].IdArticulo, f, default);
                Assert.NotEmpty(ficha.Puntos);
            }
        }
    }
}
