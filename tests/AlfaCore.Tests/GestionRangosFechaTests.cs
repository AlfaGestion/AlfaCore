using AlfaCore.Components.Shared;
using Xunit;

namespace AlfaCore.Tests;

public class GestionRangosFechaTests
{
    [Theory]
    [InlineData("ultimos-7", "2026-11-04", "2026-10-29", "2026-11-04")]
    [InlineData("trimestre", "2026-11-04", "2026-10-01", "2026-11-04")]
    [InlineData("trimestre-anterior", "2026-01-15", "2025-10-01", "2025-12-31")]
    [InlineData("mes-anterior", "2024-03-10", "2024-02-01", "2024-02-29")]
    public void RangosCruzanMesAnioYBisiesto(string key, string hoy, string desde, string hasta)
    {
        var fecha = DateTime.Parse(hoy);
        var rango = GestionRangosFecha.Calcular(key, fecha);
        Assert.Equal(DateTime.Parse(desde), rango.Desde);
        Assert.Equal(DateTime.Parse(hasta), rango.Hasta);
        Assert.Contains(GestionRangosFecha.Todos, x => x.Key == key);
    }
}
