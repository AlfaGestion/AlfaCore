using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

public sealed class PuntoVentaModoTests
{
    [Fact]
    public void Incluye_el_modo_mostrador_y_produccion_sin_reasignar_los_existentes()
    {
        Assert.Equal("MOSTRADOR", PuntoVentaModoKeys.Mostrador);
        Assert.Equal("SALON", PuntoVentaModoKeys.Salon);
        Assert.Equal("DELIVERY", PuntoVentaModoKeys.Delivery);
        Assert.Equal("MOSTRADOR_PRODUCCION", PuntoVentaModoKeys.MostradorProduccion);

        Assert.Equal(
            [PuntoVentaModoKeys.Mostrador, PuntoVentaModoKeys.MostradorProduccion, PuntoVentaModoKeys.Salon, PuntoVentaModoKeys.Delivery],
            PuntoVentaModoKeys.All);
        Assert.InRange(PuntoVentaModoKeys.MostradorProduccion.Length, 1, 20);
    }
}
