using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class VentasTopNavigationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/ALFANET/106")]
    [InlineData("/ALFANET/153")]
    public async Task InicioDelReporteVuelveAlDashboardSinMezclarPuntoDeVenta(string prefix)
    {
        var navigation = new CargaComprobantesUiTests.TestNavigation();
        var routes = new Routes(prefix);
        var items = ModuleTopNavPresets.BuildVentas(routes, navigation, "reportes");

        Assert.True(Assert.Single(items, x => x.Key == "reportes").Active);
        Assert.DoesNotContain(items, x => x.Url.Contains("punto-venta"));
        var inicio = Assert.Single(items, x => x.Key == "inicio");
        await inicio.OnClick!();
        Assert.Equal("http://localhost:5055" + prefix + "/ventas-dashboard", navigation.Uri);

        var pos = ModuleTopNavPresets.BuildPuntoVenta(routes, navigation, "punto-venta");
        var acceso = Assert.Single(pos, x => x.Key == "punto-venta");
        await acceso.OnClick!();
        Assert.Equal("http://localhost:5055" + prefix + "/ventas/punto-venta", navigation.Uri);
    }

    private sealed class Routes(string prefix) : IRouteContextService
    {
        public string BuildRoute(string route) => prefix + route;
    }
}
